using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.RootFolders;
using Sonarr.Http;
using Sonarr.Http.Extensions;

namespace Sonarr.Api.V5.Movies;

public class MovieVersionWantedResource
{
    public int MovieId { get; set; }
    public string? MovieTitle { get; set; }
    public int Year { get; set; }
    public int VersionSlotId { get; set; }
    public string? VersionLabel { get; set; }
    public int QualityProfileId { get; set; }
}

// Movies that still need a file (missing) or whose file is below the quality profile's cutoff.
[V5ApiController("wanted/movie")]
public class MovieWantedController : Controller
{
    private static readonly HashSet<string> SortKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "movieMetadata.sortTitle",
        "movies.added",
        "movies.lastSearchTime",
        "movieMetadata.year"
    };

    private readonly IMovieService _movieService;
    private readonly IMovieCutoffService _movieCutoffService;
    private readonly IRootFolderService _rootFolderService;
    private readonly IVersionSlotService _versionSlotService;

    public MovieWantedController(IMovieService movieService, IMovieCutoffService movieCutoffService, IRootFolderService rootFolderService, IVersionSlotService versionSlotService)
    {
        _movieService = movieService;
        _movieCutoffService = movieCutoffService;
        _rootFolderService = rootFolderService;
        _versionSlotService = versionSlotService;
    }

    // The monitored versions (other than the movie itself) of monitored movies that have no file yet.
    [HttpGet("missing/versions")]
    [Produces("application/json")]
    public Ok<List<MovieVersionWantedResource>> GetMissingVersions()
    {
        return TypedResults.Ok(_versionSlotService.GetMissingViews().Select(ToVersionResource).ToList());
    }

    // The monitored versions (other than the movie itself) whose file is below their profile's cutoff.
    [HttpGet("cutoff/versions")]
    [Produces("application/json")]
    public Ok<List<MovieVersionWantedResource>> GetCutoffUnmetVersions()
    {
        return TypedResults.Ok(_versionSlotService.GetCutoffUnmetViews().Select(ToVersionResource).ToList());
    }

    private static MovieVersionWantedResource ToVersionResource(Movie view)
    {
        return new MovieVersionWantedResource
        {
            MovieId = view.Id,
            MovieTitle = view.Title,
            Year = view.Year,
            VersionSlotId = view.VersionSlotId,
            VersionLabel = view.VersionLabel,
            QualityProfileId = view.QualityProfileId
        };
    }

    [HttpGet("missing")]
    [Produces("application/json")]
    public Ok<PagingResource<MovieResource>> GetMissing([FromQuery] PagingRequestResource paging, [FromQuery] bool monitored = true)
    {
        var pagingSpec = GetPagingSpec(paging, monitored);

        return TypedResults.Ok(pagingSpec.ApplyToPage(spec => _movieService.MoviesWithoutFiles(spec), MapToResource));
    }

    [HttpGet("cutoff")]
    [Produces("application/json")]
    public Ok<PagingResource<MovieResource>> GetCutoffUnmet([FromQuery] PagingRequestResource paging, [FromQuery] bool monitored = true)
    {
        var pagingSpec = GetPagingSpec(paging, monitored);

        return TypedResults.Ok(pagingSpec.ApplyToPage(spec => _movieCutoffService.MoviesWhereCutoffUnmet(spec), MapToResource));
    }

    private static PagingSpec<Movie> GetPagingSpec(PagingRequestResource paging, bool monitored)
    {
        var pagingSpec = new PagingResource<MovieResource>(paging).MapToPagingSpec<MovieResource, Movie>(SortKeys, "movieMetadata.sortTitle", SortDirection.Ascending);

        if (monitored)
        {
            pagingSpec.FilterExpressions.Add(m => m.Monitored == true);
        }
        else
        {
            pagingSpec.FilterExpressions.Add(m => m.Monitored == false);
        }

        return pagingSpec;
    }

    private MovieResource MapToResource(Movie movie)
    {
        var resource = movie.ToResource();

        resource.RootFolderPath = _rootFolderService.GetBestRootFolderPath(resource.Path ?? string.Empty);

        return resource;
    }
}

// Movies with a release (cinema, digital or physical) between two dates.
[V5ApiController("calendar/movie")]
public class MovieCalendarController : Controller
{
    private readonly IMovieService _movieService;

    public MovieCalendarController(IMovieService movieService)
    {
        _movieService = movieService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<MovieResource>> GetCalendar(DateTime? start, DateTime? end, bool includeUnmonitored = false)
    {
        var startUse = start ?? DateTime.Today;
        var endUse = end ?? DateTime.Today.AddDays(30);

        return TypedResults.Ok(_movieService.GetMoviesBetweenDates(startUse, endUse, includeUnmonitored).Select(m => m.ToResource()).ToList());
    }
}
