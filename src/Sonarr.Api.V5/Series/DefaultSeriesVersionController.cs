using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Tv.Versions;
using Sonarr.Http;

namespace Sonarr.Api.V5.Series;

// The versions every new series gets.
[V5ApiController("settings/seriesversions")]
public class DefaultSeriesVersionController : Controller
{
    private readonly IDefaultSeriesVersionService _defaultVersionService;

    public DefaultSeriesVersionController(IDefaultSeriesVersionService defaultVersionService)
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
