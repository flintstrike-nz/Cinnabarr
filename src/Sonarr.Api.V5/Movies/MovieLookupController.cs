using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource;
using Sonarr.Http;

namespace Sonarr.Api.V5.Movies;

[V5ApiController("movie/lookup")]
public class MovieLookupController : Controller
{
    private readonly ISearchForNewMovie _searchProxy;

    public MovieLookupController(ISearchForNewMovie searchProxy)
    {
        _searchProxy = searchProxy;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<IEnumerable<MovieResource>> Search([FromQuery] string term)
    {
        var results = _searchProxy.SearchForNewMovie(term);

        return TypedResults.Ok(results.Select(m => m.ToResource()));
    }
}
