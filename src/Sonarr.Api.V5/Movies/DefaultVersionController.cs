using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Movies.Versions;
using Sonarr.Http;

namespace Sonarr.Api.V5.Movies;

// The versions every new movie gets.
[V5ApiController("settings/movieversions")]
public class DefaultVersionController : Controller
{
    private readonly IDefaultVersionService _defaultVersionService;

    public DefaultVersionController(IDefaultVersionService defaultVersionService)
    {
        _defaultVersionService = defaultVersionService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<DefaultVersion>> Get()
    {
        return TypedResults.Ok(_defaultVersionService.Get());
    }

    [HttpPut]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<List<DefaultVersion>> Put([FromBody] List<DefaultVersion> versions)
    {
        _defaultVersionService.Set(versions);

        return TypedResults.Ok(_defaultVersionService.Get());
    }
}
