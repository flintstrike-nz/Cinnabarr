using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
    private readonly IEngineControlService _engineControlService;

    public EngineDownloadController(IEngineDownloadFilesService engineDownloadFilesService, IEngineControlService engineControlService)
    {
        _engineDownloadFilesService = engineDownloadFilesService;
        _engineControlService = engineControlService;
    }

    // Each file of the download: its library path, state, progress and seeding goals with where they came from.
    [HttpGet("{downloadId}/files")]
    [Produces("application/json")]
    public EngineDownloadFilesResource GetFiles([FromRoute] string downloadId)
    {
        var files = _engineDownloadFilesService.Get(downloadId) ?? throw new NotFoundException();

        return files.ToResource();
    }

    [HttpPost("{downloadId}/pause")]
    public NoContent Pause([FromRoute] string downloadId)
    {
        return Control(() => _engineControlService.Pause(downloadId));
    }

    [HttpPost("{downloadId}/resume")]
    public NoContent Resume([FromRoute] string downloadId)
    {
        return Control(() => _engineControlService.Resume(downloadId));
    }

    [HttpPost("{downloadId}/forcestart")]
    public NoContent ForceStart([FromRoute] string downloadId, [FromQuery] bool enabled = true)
    {
        return Control(() => _engineControlService.SetForceStart(downloadId, enabled));
    }

    [HttpPost("{downloadId}/recheck")]
    public NoContent Recheck([FromRoute] string downloadId)
    {
        return Control(() => _engineControlService.Recheck(downloadId));
    }

    // move: top, up, down or bottom.
    [HttpPost("{downloadId}/queue")]
    public NoContent MoveInQueue([FromRoute] string downloadId, [FromQuery] string move)
    {
        return Control(() => _engineControlService.MoveInQueue(downloadId, move));
    }

    // The download's own speed limits in KiB/s; 0 is unlimited.
    [HttpPut("{downloadId}/limits")]
    public NoContent SetLimits([FromRoute] string downloadId, [FromQuery] int download = 0, [FromQuery] int upload = 0)
    {
        return Control(() => _engineControlService.SetSpeedLimits(downloadId, download, upload));
    }

    private static NoContent Control(Action action)
    {
        try
        {
            action();
        }
        catch (EngineControlException ex)
        {
            throw new BadRequestException($"That can't be done: {ex.Message}");
        }

        return TypedResults.NoContent();
    }
}
