using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using Sonarr.Http;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace Sonarr.Api.V5.Release;

[V5ApiController("release/movie")]
public class MovieReleaseController : Controller
{
    private readonly ISearchForMovieReleases _releaseSearchService;
    private readonly IPrioritizeDownloadDecision _prioritizeDownloadDecision;
    private readonly IDownloadService _downloadService;
    private readonly Logger _logger;

    private readonly QualityProfile _qualityProfile;
    private readonly ICached<RemoteMovie> _remoteMovieCache;

    public MovieReleaseController(ISearchForMovieReleases releaseSearchService,
                                  IPrioritizeDownloadDecision prioritizeDownloadDecision,
                                  IDownloadService downloadService,
                                  ICacheManager cacheManager,
                                  IQualityProfileService qualityProfileService,
                                  Logger logger)
    {
        _releaseSearchService = releaseSearchService;
        _prioritizeDownloadDecision = prioritizeDownloadDecision;
        _downloadService = downloadService;
        _logger = logger;

        _qualityProfile = qualityProfileService.GetDefaultProfile(string.Empty);
        _remoteMovieCache = cacheManager.GetCache<RemoteMovie>(GetType(), "remoteMovies");
    }

    // An interactive search: every release found, with the decision made about it.
    [HttpGet]
    [Produces("application/json")]
    public async Task<Ok<List<MovieReleaseResource>>> GetReleases([FromQuery] int movieId)
    {
        try
        {
            var decisions = await _releaseSearchService.MovieSearch(movieId, true, true);
            var prioritized = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);

            var result = new List<MovieReleaseResource>();

            foreach (var decision in prioritized.Where(d => d.RemoteMovie != null))
            {
                var resource = decision.ToResource(result.Count, _qualityProfile);

                _remoteMovieCache.Set(GetCacheKey(decision.RemoteMovie.Release.IndexerId, decision.RemoteMovie.Release.Guid), decision.RemoteMovie, TimeSpan.FromMinutes(30));

                result.Add(resource);
            }

            return TypedResults.Ok(result);
        }
        catch (SearchFailedException ex)
        {
            throw new NzbDroneClientException(HttpStatusCode.BadRequest, ex.Message);
        }
        catch (Exception ex) when (ex is not NzbDroneClientException)
        {
            _logger.Error(ex, "Movie search failed: " + ex.Message);
            throw new NzbDroneClientException(HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<Ok<MovieReleaseGrabResource>> DownloadRelease([FromBody] MovieReleaseGrabResource release)
    {
        var remoteMovie = _remoteMovieCache.Find(GetCacheKey(release.IndexerId, release.Guid));

        if (remoteMovie == null)
        {
            _logger.Debug("Couldn't find requested release in cache, cache timeout probably expired.");

            throw new NzbDroneClientException(HttpStatusCode.NotFound, "Couldn't find requested release in cache, try searching again");
        }

        if (remoteMovie.Movie == null)
        {
            throw new NzbDroneClientException(HttpStatusCode.NotFound, "Unable to find a matching movie for the release");
        }

        try
        {
            await _downloadService.DownloadReport(remoteMovie, release.DownloadClientId);
        }
        catch (ReleaseDownloadException ex)
        {
            _logger.Error(ex, ex.Message);
            throw new NzbDroneClientException(HttpStatusCode.Conflict, $"Getting release from indexer failed: {ex.Message}");
        }

        return TypedResults.Ok(release);
    }

    private static string GetCacheKey(int indexerId, string guid)
    {
        return string.Concat(indexerId, "_", guid);
    }
}
