using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Versions;
using NzbDrone.Core.Validation;
using Sonarr.Http;
using Sonarr.Http.REST;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace Sonarr.Api.V5.Release;

[V5ApiController]
public class ReleaseController : RestController<ReleaseResource>
{
    private readonly IFetchAndParseRss _rssFetcherAndParser;
    private readonly ISearchForReleases _releaseSearchService;
    private readonly IMakeDownloadDecision _downloadDecisionMaker;
    private readonly IPrioritizeDownloadDecision _prioritizeDownloadDecision;
    private readonly IDownloadService _downloadService;
    private readonly ISeriesService _seriesService;
    private readonly IEpisodeService _episodeService;
    private readonly IParsingService _parsingService;
    private readonly ISeriesVersionSlotService _versionSlotService;
    private readonly IHistoryService _historyService;
    private readonly Logger _logger;

    private readonly QualityProfile _qualityProfile;
    private readonly ICached<RemoteEpisode> _remoteEpisodeCache;

    public ReleaseController(IFetchAndParseRss rssFetcherAndParser,
                         ISearchForReleases releaseSearchService,
                         IMakeDownloadDecision downloadDecisionMaker,
                         IPrioritizeDownloadDecision prioritizeDownloadDecision,
                         IDownloadService downloadService,
                         ISeriesService seriesService,
                         IEpisodeService episodeService,
                         IParsingService parsingService,
                         ISeriesVersionSlotService versionSlotService,
                         IHistoryService historyService,
                         ICacheManager cacheManager,
                         IQualityProfileService qualityProfileService,
                         Logger logger)
    {
        _rssFetcherAndParser = rssFetcherAndParser;
        _releaseSearchService = releaseSearchService;
        _downloadDecisionMaker = downloadDecisionMaker;
        _prioritizeDownloadDecision = prioritizeDownloadDecision;
        _downloadService = downloadService;
        _seriesService = seriesService;
        _episodeService = episodeService;
        _parsingService = parsingService;
        _versionSlotService = versionSlotService;
        _historyService = historyService;
        _logger = logger;

        _qualityProfile = qualityProfileService.GetDefaultProfile(string.Empty);
        _remoteEpisodeCache = cacheManager.GetCache<RemoteEpisode>(GetType(), "remoteEpisodes");

        PostValidator.RuleFor(s => s.Release).NotNull();
        PostValidator.RuleFor(s => s.Release!.IndexerId).ValidId();
        PostValidator.RuleFor(s => s.Release!.Guid).NotEmpty();
    }

    [NonAction]
    public override Results<Ok<ReleaseResource>, NotFound> GetResourceByIdWithErrorHandler(int id)
    {
        return base.GetResourceByIdWithErrorHandler(id);
    }

    protected override ReleaseResource GetResourceById(int id)
    {
        throw new NotImplementedException();
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<Results<Ok<ReleaseGrabResource>, NotFound>> DownloadRelease([FromBody] ReleaseGrabResource release)
    {
        var remoteEpisode = _remoteEpisodeCache.Find(GetCacheKey(release));

        if (remoteEpisode == null)
        {
            _logger.Debug("Couldn't find requested release in cache, cache timeout probably expired.");

            throw new NzbDroneClientException(HttpStatusCode.NotFound, "Couldn't find requested release in cache, try searching again");
        }

        // A release found by a search for one of the series' extra versions is grabbed for that version.
        var versionSlotId = remoteEpisode.Series?.VersionSlotId ?? release.SearchInfo?.VersionSlotId ?? 0;

        try
        {
            if (release.Override != null)
            {
                var overrideInfo = release.Override;

                Ensure.That(overrideInfo.SeriesId, () => release.Override.SeriesId).IsNotNull();
                Ensure.That(overrideInfo.EpisodeIds, () => overrideInfo.EpisodeIds).IsNotNull();
                Ensure.That(overrideInfo.EpisodeIds, () => overrideInfo.EpisodeIds).HasItems();
                Ensure.That(overrideInfo.Quality, () => overrideInfo.Quality).IsNotNull();
                Ensure.That(overrideInfo.Languages, () => overrideInfo.Languages).IsNotNull();

                // Clone the remote episode so we don't overwrite anything on the original
                remoteEpisode = new RemoteEpisode
                {
                    Release = remoteEpisode.Release,
                    ParsedEpisodeInfo = remoteEpisode.ParsedEpisodeInfo.JsonClone(),
                    SceneMapping = remoteEpisode.SceneMapping,
                    MappedSeasonNumber = remoteEpisode.MappedSeasonNumber,
                    EpisodeRequested = remoteEpisode.EpisodeRequested,
                    DownloadAllowed = remoteEpisode.DownloadAllowed,
                    SeedConfiguration = remoteEpisode.SeedConfiguration,
                    CustomFormats = remoteEpisode.CustomFormats,
                    CustomFormatScore = remoteEpisode.CustomFormatScore,
                    SeriesMatchType = remoteEpisode.SeriesMatchType,
                    ReleaseSource = remoteEpisode.ReleaseSource
                };

                remoteEpisode.Series = _seriesService.GetSeries(overrideInfo.SeriesId!.Value);
                remoteEpisode.Episodes = _episodeService.GetEpisodes(overrideInfo.EpisodeIds);
                remoteEpisode.ParsedEpisodeInfo.Quality = overrideInfo.Quality;
                remoteEpisode.Languages = overrideInfo.Languages;
            }

            if (remoteEpisode.Series == null)
            {
                if (release.SearchInfo?.EpisodeId.HasValue == true)
                {
                    var episode = _episodeService.GetEpisode(release.SearchInfo.EpisodeId.Value);

                    remoteEpisode.Series = _seriesService.GetSeries(episode.SeriesId);
                    remoteEpisode.Episodes = new List<Episode> { episode };
                }
                else if (release.SearchInfo?.SeriesId.HasValue == true)
                {
                    var series = _seriesService.GetSeries(release.SearchInfo.SeriesId.Value);
                    var episodes = _parsingService.GetEpisodes(remoteEpisode.ParsedEpisodeInfo, series, true);

                    if (episodes.Empty())
                    {
                        throw new NzbDroneClientException(HttpStatusCode.NotFound, "Unable to parse episodes in the release, will need to be manually provided");
                    }

                    remoteEpisode.Series = series;
                    remoteEpisode.Episodes = episodes;
                }
                else
                {
                    throw new NzbDroneClientException(HttpStatusCode.NotFound, "Unable to find matching series and episodes, will need to be manually provided");
                }
            }
            else if (remoteEpisode.Episodes.Empty())
            {
                var episodes = _parsingService.GetEpisodes(remoteEpisode.ParsedEpisodeInfo, remoteEpisode.Series, true);

                if (episodes.Empty() && release.SearchInfo?.EpisodeId.HasValue == true)
                {
                    var episode = _episodeService.GetEpisode(release.SearchInfo.EpisodeId.Value);

                    episodes = new List<Episode> { episode };
                }

                remoteEpisode.Episodes = episodes;
            }

            if (remoteEpisode.Episodes.Empty())
            {
                throw new NzbDroneClientException(HttpStatusCode.NotFound, "Unable to parse episodes in the release, will need to be manually provided");
            }

            if (versionSlotId != 0 && remoteEpisode.Series.VersionSlotId == 0)
            {
                remoteEpisode.Series = _versionSlotService.GetView(remoteEpisode.Series, versionSlotId);
                remoteEpisode.Episodes = _versionSlotService.GetEpisodeViews(remoteEpisode.Episodes, versionSlotId);
            }

            await _downloadService.DownloadReport(remoteEpisode, release.Override?.DownloadClientId);
        }
        catch (ReleaseDownloadException ex)
        {
            _logger.Error(ex, ex.Message);
            throw new NzbDroneClientException(HttpStatusCode.Conflict, $"Getting release from indexer failed: {ex.Message}");
        }

        return TypedResults.Ok(release);
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Results<Ok<List<ReleaseResource>>, BadRequest>> GetReleases(int? seriesId, int? episodeId, int? seasonNumber, int versionSlotId = 0)
    {
        if (episodeId.HasValue)
        {
            return TypedResults.Ok(await GetEpisodeReleases(episodeId.Value, versionSlotId));
        }

        if (seriesId.HasValue && seasonNumber.HasValue)
        {
            return TypedResults.Ok(await GetSeasonReleases(seriesId.Value, seasonNumber.Value, versionSlotId));
        }

        if (seriesId.HasValue || seasonNumber.HasValue)
        {
            throw new NzbDroneClientException(HttpStatusCode.BadRequest, "seriesId and seasonNumber must be provided together");
        }

        return TypedResults.Ok(await GetRss());
    }

    private async Task<List<ReleaseResource>> GetEpisodeReleases(int episodeId, int versionSlotId)
    {
        try
        {
            var decisions = await _releaseSearchService.EpisodeSearch(episodeId, versionSlotId, true, true);
            var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);
            var history = _historyService.FindByEpisodeId(episodeId).Where(h => h.VersionSlotId == versionSlotId).ToList();

            return MapDecisions(prioritizedDecisions, history);
        }
        catch (SearchFailedException ex)
        {
            throw new NzbDroneClientException(HttpStatusCode.BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Episode search failed: " + ex.Message);
            throw new NzbDroneClientException(HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    private async Task<List<ReleaseResource>> GetSeasonReleases(int seriesId, int seasonNumber, int versionSlotId)
    {
        try
        {
            var decisions = await _releaseSearchService.SeasonSearch(seriesId, versionSlotId, seasonNumber, false, false, true, true);
            var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);
            var history = _historyService.GetBySeason(seriesId, seasonNumber, null).Where(h => h.VersionSlotId == versionSlotId).ToList();

            return MapDecisions(prioritizedDecisions, history);
        }
        catch (SearchFailedException ex)
        {
            throw new NzbDroneClientException(HttpStatusCode.BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Season search failed: " + ex.Message);
            throw new NzbDroneClientException(HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    private async Task<List<ReleaseResource>> GetRss()
    {
        var reports = await _rssFetcherAndParser.Fetch();

        // The RSS decisions also hold one per extra version of each series; the list shows each release once.
        var decisions = _downloadDecisionMaker.GetRssDecision(reports).Where(d => (d.RemoteEpisode?.Series?.VersionSlotId ?? 0) == 0).ToList();
        var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);

        return MapDecisions(prioritizedDecisions, new List<EpisodeHistory>());
    }

    private string GetCacheKey(ReleaseResource resource)
    {
        return string.Concat(resource.Release!.IndexerId, "_", resource.Release!.Guid);
    }

    private string GetCacheKey(ReleaseGrabResource resource)
    {
        return string.Concat(resource.IndexerId, "_", resource.Guid);
    }

    private List<ReleaseResource> MapDecisions(IEnumerable<DownloadDecision> decisions, List<EpisodeHistory> history)
    {
        var result = new List<ReleaseResource>();

        foreach (var downloadDecision in decisions)
        {
            var release = downloadDecision.MapDecision(result.Count, _qualityProfile);

            release.History = AddHistory(downloadDecision.RemoteEpisode.Release, history);
            _remoteEpisodeCache.Set(GetCacheKey(release), downloadDecision.RemoteEpisode, TimeSpan.FromMinutes(30));

            result.Add(release);
        }

        return result;
    }

    private ReleaseHistoryResource? AddHistory(ReleaseInfo release, List<EpisodeHistory> history)
    {
        var grabbed = history.FirstOrDefault(h => h.EventType == EpisodeHistoryEventType.Grabbed &&
                                                  ((h.Data.TryGetValue("guid", out var guid) && guid == release.Guid) ||
                                                   (h.Data.TryGetValue("nzbInfoUrl", out var infoUrl) && infoUrl.IsNotNullOrWhiteSpace() && infoUrl.Equals(release.InfoUrl, StringComparison.Ordinal))));

        if (grabbed == null && release.DownloadProtocol == DownloadProtocol.Torrent)
        {
            if (release is not TorrentInfo torrentInfo)
            {
                return null;
            }

            if (torrentInfo.InfoHash.IsNotNullOrWhiteSpace())
            {
                grabbed = history.FirstOrDefault(h => h.EventType == EpisodeHistoryEventType.Grabbed &&
                                                      ReleaseComparer.SameTorrent(new ReleaseComparerModel(h),
                                                          torrentInfo));
            }

            if (grabbed == null)
            {
                grabbed = history.FirstOrDefault(h => h.EventType == EpisodeHistoryEventType.Grabbed &&
                                                      h.SourceTitle == release.Title &&
                                                      (DownloadProtocol)Convert.ToInt32(
                                                          h.Data.GetValueOrDefault("protocol")) ==
                                                      DownloadProtocol.Torrent &&
                                                      ReleaseComparer.SameTorrent(new ReleaseComparerModel(h),
                                                          torrentInfo));
            }
        }
        else if (grabbed == null)
        {
            grabbed = history.FirstOrDefault(h => h.EventType == EpisodeHistoryEventType.Grabbed &&
                                                  ReleaseComparer.SameNzb(new ReleaseComparerModel(h),
                                                      release));
        }

        if (grabbed != null)
        {
            var resource = new ReleaseHistoryResource
            {
                Grabbed = grabbed.Date,
            };

            var failedHistory = history.FirstOrDefault(h => h.EventType == EpisodeHistoryEventType.DownloadFailed &&
                                                            h.DownloadId == grabbed.DownloadId);

            if (failedHistory != null)
            {
                resource.Failed = failedHistory.Date;
            }

            return resource;
        }

        return null;
    }
}
