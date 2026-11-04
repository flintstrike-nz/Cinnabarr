using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Download.Engine;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Engine;

public class EngineStatusResource
{
    // Bytes per second.
    public long DownloadSpeed { get; set; }
    public long UploadSpeed { get; set; }
    public bool AlternativeSpeedLimits { get; set; }
}

// The built-in engine as a whole (roadmap §6.2): live speeds, the alternative speed limits, and pause or resume all.
[V5ApiController("engine")]
public class EngineController : Controller
{
    private readonly IEngineControlService _engineControlService;

    public EngineController(IEngineControlService engineControlService)
    {
        _engineControlService = engineControlService;
    }

    [HttpGet("status")]
    [Produces("application/json")]
    public EngineStatusResource GetStatus()
    {
        var status = Control(_engineControlService.GetStatus);

        return new EngineStatusResource
        {
            DownloadSpeed = status.DownloadSpeed,
            UploadSpeed = status.UploadSpeed,
            AlternativeSpeedLimits = status.AlternativeSpeedLimits
        };
    }

    [HttpPut("altspeed")]
    public NoContent SetAlternativeSpeedLimits([FromQuery] bool enabled)
    {
        Control(() => _engineControlService.SetAlternativeSpeedLimits(enabled));

        return TypedResults.NoContent();
    }

    [HttpPost("pause")]
    public NoContent PauseAll()
    {
        Control(_engineControlService.PauseAll);

        return TypedResults.NoContent();
    }

    [HttpPost("resume")]
    public NoContent ResumeAll()
    {
        Control(_engineControlService.ResumeAll);

        return TypedResults.NoContent();
    }

    private static void Control(Action action)
    {
        Control(() =>
        {
            action();

            return true;
        });
    }

    private static T Control<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (EngineControlException ex)
        {
            throw new BadRequestException($"That can't be done: {ex.Message}");
        }
    }
}
