using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Download.Engine;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Engine;

public class EngineAddMagnetResource
{
    public string? MagnetLink { get; set; }
}

public class EngineAddedTorrentResource
{
    public required string DownloadId { get; set; }
}

// Adding a torrent to the built-in engine by hand (roadmap §6.2). It is listed as Unlinked; Manual Import links it.
[V5ApiController("engine/torrents")]
public class EngineTorrentController : Controller
{
    private const long MaxTorrentFileBytes = 10 * 1024 * 1024;

    private readonly IEngineControlService _engineControlService;

    public EngineTorrentController(IEngineControlService engineControlService)
    {
        _engineControlService = engineControlService;
    }

    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    public EngineAddedTorrentResource AddMagnet([FromBody] EngineAddMagnetResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.MagnetLink))
        {
            throw new BadRequestException("A magnet link must be provided");
        }

        return Add(() => _engineControlService.AddMagnet(resource.MagnetLink));
    }

    [HttpPost("file")]
    [Produces("application/json")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxTorrentFileBytes)]
    public EngineAddedTorrentResource AddFile()
    {
        var files = Request.Form.Files;

        if (files.Count == 0)
        {
            throw new BadRequestException("A .torrent file must be provided");
        }

        var file = files[0];

        if (file.Length > MaxTorrentFileBytes)
        {
            throw new BadRequestException("The .torrent file is too large");
        }

        using var stream = new MemoryStream();
        file.CopyTo(stream);

        return Add(() => _engineControlService.AddTorrentFile(Path.GetFileName(file.FileName), stream.ToArray()));
    }

    private static EngineAddedTorrentResource Add(Func<string> add)
    {
        try
        {
            return new EngineAddedTorrentResource { DownloadId = add() };
        }
        catch (EngineControlException ex)
        {
            throw new BadRequestException($"The torrent can't be added: {ex.Message}");
        }
    }
}
