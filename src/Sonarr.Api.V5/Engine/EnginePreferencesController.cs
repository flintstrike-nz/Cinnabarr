using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Download.Engine;
using Sonarr.Http;

namespace Sonarr.Api.V5.Engine;

// The built-in engine's network, speed, queue and privacy settings (roadmap SET-04).
[V5ApiController("engine/preferences")]
public class EnginePreferencesController : Controller
{
    private readonly IEngineControlService _engineControlService;

    public EnginePreferencesController(IEngineControlService engineControlService)
    {
        _engineControlService = engineControlService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<EnginePreferences> GetPreferences()
    {
        return TypedResults.Ok(Control(_engineControlService.GetPreferences));
    }

    [HttpPut]
    [Consumes("application/json")]
    public NoContent SavePreferences([FromBody] EnginePreferences preferences)
    {
        Control(() =>
        {
            _engineControlService.SetPreferences(preferences);

            return true;
        });

        return TypedResults.NoContent();
    }

    private static T Control<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (EngineControlException ex)
        {
            throw new Sonarr.Http.REST.BadRequestException(ex.Message);
        }
    }
}
