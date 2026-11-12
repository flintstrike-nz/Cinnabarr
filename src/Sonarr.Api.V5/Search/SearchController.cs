using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv.Versions;
using Sonarr.Api.V5.Release;
using Sonarr.Http;
using Sonarr.Http.REST;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace Sonarr.Api.V5.Search;

public class SearchResultResource : RestResource
{
    // "series", "movie", or "unknown" when the release matched nothing in the library.
    public required string MediaType { get; set; }
    public int? MappedSeriesId { get; set; }
    public int? MappedMovieId { get; set; }
    public string? MappedTitle { get; set; }

    // What the release looks like when nothing in the library matched: "series" or "movie" with the parsed title (and year).
    public string? GuessedMediaType { get; set; }
    public string? ParsedTitle { get; set; }
    public int ParsedYear { get; set; }
    public ReleaseInfoResource? Release { get; set; }
    public ReleaseDecisionResource? Decision { get; set; }
    public QualityModel? Quality { get; set; }
    public List<Language> Languages { get; set; } = [];
    public bool DownloadAllowed { get; set; }
}

public class SearchGrabResource
{
    public required string Guid { get; set; }
    public required int IndexerId { get; set; }
    public int? DownloadClientId { get; set; }

    // For a movie or series release: the version of it that the release is for (0 or left out is the movie or series
    // itself), or a new version to make for it (a label and a quality profile).
    public int VersionSlotId { get; set; }
    public string? NewVersionLabel { get; set; }
    public int NewVersionQualityProfileId { get; set; }
}

[V5ApiController("search")]
public class SearchController : Controller
{
    private readonly ISearchForFreeText _searchService;
    private readonly IVersionSlotService _versionSlotService;
    private readonly ISeriesVersionSlotService _seriesVersionSlotService;
    private readonly IDownloadService _downloadService;
    private readonly Logger _logger;
    private readonly ICached<RemoteMedia> _remoteCache;

    public SearchController(ISearchForFreeText searchService,
                            IVersionSlotService versionSlotService,
                            ISeriesVersionSlotService seriesVersionSlotService,
                            IDownloadService downloadService,
                            ICacheManager cacheManager,
                            Logger logger)
    {
        _searchService = searchService;
        _versionSlotService = versionSlotService;
        _seriesVersionSlotService = seriesVersionSlotService;
        _downloadService = downloadService;
        _logger = logger;
        _remoteCache = cacheManager.GetCache<RemoteMedia>(GetType(), "remotes");
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Ok<List<SearchResultResource>>> Search([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new Sonarr.Http.REST.BadRequestException("query must be provided");
        }

        try
        {
            var decisions = await _searchService.Search(query);
            var result = new List<SearchResultResource>();

            foreach (var decision in decisions.Where(d => d.Remote?.Release != null))
            {
                var remote = decision.Remote!;

                _remoteCache.Set(GetCacheKey(remote.Release.IndexerId, remote.Release.Guid), remote, TimeSpan.FromMinutes(30));

                result.Add(ToResource(decision, result.Count));
            }

            return TypedResults.Ok(result);
        }
        catch (SearchFailedException ex)
        {
            throw new NzbDroneClientException(HttpStatusCode.BadRequest, ex.Message);
        }
        catch (Exception ex) when (ex is not NzbDroneClientException)
        {
            _logger.Error(ex, "Search failed: " + ex.Message);
            throw new NzbDroneClientException(HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    [HttpPost("grab")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<Ok<SearchGrabResource>> Grab([FromBody] SearchGrabResource release)
    {
        var remote = _remoteCache.Find(GetCacheKey(release.IndexerId, release.Guid));

        if (remote == null)
        {
            throw new NzbDroneClientException(HttpStatusCode.NotFound, "Couldn't find requested release in cache, try searching again");
        }

        var isMatched = remote switch
        {
            RemoteEpisode episode => episode.Series != null && episode.Episodes.Count > 0,
            RemoteMovie movie => movie.Movie != null,
            _ => false
        };

        if (!isMatched)
        {
            throw new NzbDroneClientException(HttpStatusCode.NotFound, "This release doesn't match a series or movie in the library, add it first");
        }

        // "Download another version": the release is for one of the movie's versions, which may be made now.
        NzbDrone.Core.Movies.Movie? movieOriginal = null;

        if (remote is RemoteMovie remoteMovie && (release.VersionSlotId != 0 || !string.IsNullOrWhiteSpace(release.NewVersionLabel)))
        {
            var slotId = release.VersionSlotId;

            if (slotId == 0)
            {
                slotId = _versionSlotService.Add(new VersionSlot
                {
                    MovieId = remoteMovie.Movie.Id,
                    Label = release.NewVersionLabel,
                    QualityProfileId = release.NewVersionQualityProfileId,
                    Monitored = true
                }).Id;
            }

            movieOriginal = remoteMovie.Movie;
            remoteMovie.Movie = _versionSlotService.GetView(remoteMovie.Movie, slotId);
        }

        // The same for a series release: it is grabbed for one of the series' versions, which may be made now.
        NzbDrone.Core.Tv.Series? seriesOriginal = null;
        List<NzbDrone.Core.Tv.Episode>? episodesOriginal = null;

        if (remote is RemoteEpisode remoteEpisode && (release.VersionSlotId != 0 || !string.IsNullOrWhiteSpace(release.NewVersionLabel)))
        {
            var slotId = release.VersionSlotId;

            if (slotId == 0)
            {
                slotId = _seriesVersionSlotService.Add(new SeriesVersionSlot
                {
                    SeriesId = remoteEpisode.Series.Id,
                    Label = release.NewVersionLabel,
                    QualityProfileId = release.NewVersionQualityProfileId,
                    Monitored = true
                }).Id;
            }

            seriesOriginal = remoteEpisode.Series;
            episodesOriginal = remoteEpisode.Episodes;
            remoteEpisode.Series = _seriesVersionSlotService.GetView(remoteEpisode.Series, slotId);
            remoteEpisode.Episodes = _seriesVersionSlotService.GetEpisodeViews(remoteEpisode.Episodes, slotId);
        }

        try
        {
            await _downloadService.DownloadReport(remote, release.DownloadClientId);
        }
        catch (ReleaseDownloadException ex)
        {
            _logger.Error(ex, ex.Message);
            throw new NzbDroneClientException(HttpStatusCode.Conflict, $"Getting release from indexer failed: {ex.Message}");
        }
        finally
        {
            // The cached release stays the movie's or series' own, so it can still be grabbed for another version.
            if (movieOriginal != null && remote is RemoteMovie grabbed)
            {
                grabbed.Movie = movieOriginal;
            }

            if (seriesOriginal != null && episodesOriginal != null && remote is RemoteEpisode grabbedEpisode)
            {
                grabbedEpisode.Series = seriesOriginal;
                grabbedEpisode.Episodes = episodesOriginal;
            }
        }

        return TypedResults.Ok(release);
    }

    private static SearchResultResource ToResource(DownloadDecision decision, int id)
    {
        var resource = new SearchResultResource
        {
            Id = id,
            MediaType = "unknown",
            Release = decision.Remote!.Release.ToResource(),
            Decision = new ReleaseDecisionResource(decision)
        };

        if (decision.RemoteEpisode is { } episode)
        {
            resource.Quality = episode.ParsedEpisodeInfo?.Quality;
            resource.Languages = episode.Languages ?? [];

            if (episode.ParsedEpisodeInfo?.SeriesTitle != null)
            {
                resource.GuessedMediaType = "series";
                resource.ParsedTitle = episode.ParsedEpisodeInfo.SeriesTitle;
                resource.ParsedYear = episode.ParsedEpisodeInfo.SeriesTitleInfo?.Year ?? 0;
            }

            if (episode.Series != null)
            {
                resource.MediaType = "series";
                resource.MappedSeriesId = episode.Series.Id;
                resource.MappedTitle = episode.Series.Title;
                resource.DownloadAllowed = episode.Episodes.Count > 0;
            }
        }
        else if (decision.RemoteMovie is { } movie)
        {
            resource.Quality = movie.ParsedMovieInfo?.Quality;
            resource.Languages = movie.Languages ?? [];

            if (movie.ParsedMovieInfo != null)
            {
                resource.GuessedMediaType = "movie";
                resource.ParsedTitle = movie.ParsedMovieInfo.PrimaryMovieTitle;
                resource.ParsedYear = movie.ParsedMovieInfo.Year;
            }

            if (movie.Movie != null)
            {
                resource.MediaType = "movie";
                resource.MappedMovieId = movie.Movie.Id;
                resource.MappedTitle = movie.Movie.Title;
                resource.DownloadAllowed = true;
            }
        }

        return resource;
    }

    private static string GetCacheKey(int indexerId, string guid)
    {
        return string.Concat(indexerId, "_", guid);
    }
}
