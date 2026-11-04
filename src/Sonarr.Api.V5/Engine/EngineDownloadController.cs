using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Download.Engine;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Engine;

// Built-in engine downloads in detail (roadmap §6.2).
[V5ApiController("engine/downloads")]
public class EngineDownloadController : Controller
{
    private readonly IEngineDownloadFilesService _engineDownloadFilesService;

    public EngineDownloadController(IEngineDownloadFilesService engineDownloadFilesService)
    {
        _engineDownloadFilesService = engineDownloadFilesService;
    }

    // Each file of the download: its library path, state, progress and seeding goals with where they came from.
    [HttpGet("{downloadId}/files")]
    [Produces("application/json")]
    public EngineDownloadFilesResource GetFiles([FromRoute] string downloadId)
    {
        var files = _engineDownloadFilesService.Get(downloadId) ?? throw new NotFoundException();

        return files.ToResource();
    }
}
