using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Movies;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Release;

[V5ApiController("release/push")]
public class ReleasePushController : RestController<ReleasePushResource>
{
    private readonly IMakeDownloadDecision _downloadDecisionMaker;
    private readonly IMakeMovieDownloadDecision _movieDecisionMaker;
    private readonly IMovieService _movieService;
    private readonly IVersionSlotService _versionSlotService;
    private readonly IProcessDownloadDecisions _downloadDecisionProcessor;
    private readonly IIndexerFactory _indexerFactory;
    private readonly IDownloadClientFactory _downloadClientFactory;
    private readonly Logger _logger;

    private readonly QualityProfile _qualityProfile;

    private static readonly object PushLock = new object();

    public ReleasePushController(IMakeDownloadDecision downloadDecisionMaker,
                             IMakeMovieDownloadDecision movieDecisionMaker,
                             IMovieService movieService,
                             IVersionSlotService versionSlotService,
                             IProcessDownloadDecisions downloadDecisionProcessor,
                             IIndexerFactory indexerFactory,
                             IDownloadClientFactory downloadClientFactory,
                             IQualityProfileService qualityProfileService,
                             Logger logger)
    {
        _downloadDecisionMaker = downloadDecisionMaker;
        _movieDecisionMaker = movieDecisionMaker;
        _movieService = movieService;
        _versionSlotService = versionSlotService;
        _downloadDecisionProcessor = downloadDecisionProcessor;
        _indexerFactory = indexerFactory;
        _downloadClientFactory = downloadClientFactory;
        _logger = logger;

        _qualityProfile = qualityProfileService.GetDefaultProfile(string.Empty);

        PostValidator.RuleFor(s => s.Title).NotEmpty();
        PostValidator.RuleFor(s => s.DownloadUrl).NotEmpty().When(s => s.MagnetUrl.IsNullOrWhiteSpace());
        PostValidator.RuleFor(s => s.MagnetUrl).NotEmpty().When(s => s.DownloadUrl.IsNullOrWhiteSpace());
        PostValidator.RuleFor(s => s.Protocol).NotEmpty();
        PostValidator.RuleFor(s => s.PublishDate).NotEmpty();
    }

    [HttpPost]
    [Consumes("application/json")]
    public Results<Ok<ReleaseResource>, Ok<MovieReleaseResource>, BadRequest> Create([FromBody] ReleasePushResource release)
    {
        _logger.Info("Release pushed: {0} - {1}", release.Title, release.DownloadUrl ?? release.MagnetUrl);

        ValidateResource(release);

        var info = release.ToModel();

        info.Guid = "PUSH-" + info.DownloadUrl;

        ResolveIndexer(info);

        var downloadClientId = ResolveDownloadClientId(release);

        DownloadDecision? decision;

        lock (PushLock)
        {
            if (release.VersionSlotId != 0)
            {
                // A release for one version of a movie is decided as that version (its profile and file), as a search for it would be.
                var view = _versionSlotService.GetView(_movieService.GetMovie(release.MovieId), release.VersionSlotId);

                decision = _movieDecisionMaker.GetSearchDecision(new List<ReleaseInfo> { info }, view, true).FirstOrDefault();
            }
            else
            {
                // A pushed release is for a series or for a movie; the one that finds its title in the library wins.
                var seriesDecision = _downloadDecisionMaker.GetRssDecision(new List<ReleaseInfo> { info }, true).FirstOrDefault();
                var movieDecision = _movieDecisionMaker.GetRssDecision(new List<ReleaseInfo> { info }, true).FirstOrDefault();

                decision = FreeTextSearchService.Pick(seriesDecision, movieDecision);
            }

            _downloadDecisionProcessor.ProcessDecision(decision, downloadClientId).GetAwaiter().GetResult();
        }

        if (decision?.RemoteMovie != null)
        {
            if (decision.RemoteMovie.ParsedMovieInfo == null)
            {
                throw new ValidationException(new List<ValidationFailure> { new("Title", "Unable to parse", release.Title) });
            }

            return TypedResults.Ok(decision.ToResource(1, _qualityProfile));
        }

        if (decision?.RemoteEpisode?.ParsedEpisodeInfo == null)
        {
            throw new ValidationException(new List<ValidationFailure> { new("Title", "Unable to parse", release.Title) });
        }

        return TypedResults.Ok(decision.MapDecision(1, _qualityProfile));
    }

    private void ResolveIndexer(ReleaseInfo release)
    {
        var indexer = _indexerFactory.ResolveIndexer(release.IndexerId, release.Indexer);

        if (indexer == null)
        {
            _logger.Debug("Push Release {0} not associated with an indexer.", release.Title);
        }
        else
        {
            _logger.Debug("Push Release {0} associated with indexer '{1} ({2})", release.Title, indexer.Name, indexer.Id);

            release.IndexerId = indexer.Id;
            release.Indexer = indexer.Name;
        }
    }

    private int? ResolveDownloadClientId(ReleasePushResource release)
    {
        var downloadClient = _downloadClientFactory.ResolveDownloadClient(release.DownloadClientId, release.DownloadClientName);

        if (downloadClient == null)
        {
            _logger.Debug("Push Release {0} not associated with a download client.", release.Title);
        }
        else
        {
            _logger.Debug("Push Release {0} associated with download client '{1} ({2})", release.Title, downloadClient.Name, downloadClient.Id);
        }

        return downloadClient?.Id;
    }
}
