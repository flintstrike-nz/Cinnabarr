using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Engine;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Engine;

// Review and approval of the orphan cleanup (roadmap §4.13): nothing is deleted until a list has been approved.
[V5ApiController("engine/orphans")]
public class EngineOrphanController : Controller
{
    private readonly IEngineOrphanService _engineOrphanService;
    private readonly IConfigService _configService;

    public EngineOrphanController(IEngineOrphanService engineOrphanService, IConfigService configService)
    {
        _engineOrphanService = engineOrphanService;
        _configService = configService;
    }

    [HttpGet]
    [Produces("application/json")]
    public EngineOrphansResource Get()
    {
        return ToResource(Scan());
    }

    [HttpPost("delete")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public EngineOrphansResource Delete([FromBody] EngineOrphanDeleteResource resource)
    {
        try
        {
            _engineOrphanService.Approve(resource.Paths ?? new List<string>(), resource.DeleteAutomatically);
        }
        catch (EngineOrphanException ex)
        {
            throw new BadRequestException($"Orphaned partial files can't be deleted: {ex.Message}");
        }

        return ToResource(Scan());
    }

    private List<EngineOrphanFile> Scan()
    {
        try
        {
            return _engineOrphanService.Scan();
        }
        catch (EngineOrphanException ex)
        {
            throw new BadRequestException($"Orphaned partial files can't be looked for: {ex.Message}");
        }
    }

    private EngineOrphansResource ToResource(List<EngineOrphanFile> files)
    {
        return new EngineOrphansResource
        {
            GraceDays = _configService.EngineOrphanGraceDays,
            DeleteAutomatically = _configService.EngineOrphanCleanupApproved,
            Files = files.Select(f => f.ToResource()).ToList()
        };
    }
}
