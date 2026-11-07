using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;
using Sonarr.Http;

namespace Sonarr.Api.V5.Movies;

[V5ApiController("movie/lookup")]
public class MovieLookupController : Controller
{
    private readonly ISearchForNewMovie _searchProxy;
    private readonly IProvideMovieInfo _movieInfo;
    private readonly IMovieService _movieService;

    public MovieLookupController(ISearchForNewMovie searchProxy, IProvideMovieInfo movieInfo, IMovieService movieService)
    {
        _searchProxy = searchProxy;
        _movieInfo = movieInfo;
        _movieService = movieService;
    }

    // What is trending or popular on TMDb, with the movies already in the library marked as such.
    [HttpGet("discover")]
    [Produces("application/json")]
    public Ok<IEnumerable<MovieResource>> Discover([FromQuery] string list = "trending")
    {
        var found = string.Equals(list, "popular", StringComparison.OrdinalIgnoreCase)
            ? _movieInfo.GetPopularMovies()
            : _movieInfo.GetTrendingMovies();

        return TypedResults.Ok(found.Select(metadata => (_movieService.FindByTmdbId(metadata.TmdbId) ?? new Movie { MovieMetadata = metadata }).ToResource()));
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<IEnumerable<MovieResource>> Search([FromQuery] string term)
    {
        var results = _searchProxy.SearchForNewMovie(term);

        return TypedResults.Ok(results.Select(m => m.ToResource()));
    }
}
