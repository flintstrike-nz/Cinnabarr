using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using Sonarr.Http;

namespace Sonarr.Api.V5.Engine;

public class EngineLegalNoticeResource
{
    public bool Accepted { get; set; }
    public required string Text { get; set; }
}

// The built-in engine shares what it downloads with other people. The owner is told once, on first run (roadmap SET-03).
[V5ApiController("engine/legalnotice")]
public class EngineLegalNoticeController : Controller
{
    private readonly IConfigService _configService;

    public EngineLegalNoticeController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<EngineLegalNoticeResource> GetNotice()
    {
        return TypedResults.Ok(new EngineLegalNoticeResource
        {
            Accepted = _configService.EngineLegalNoticeAccepted,
            Text = $"{BuildInfo.AppName}'s built-in download engine is a file sharing program. When you download a torrent, its data is made available to others by uploading it. Any content you download or share is your sole responsibility. This notice is shown once."
        });
    }

    [HttpPost]
    public NoContent Accept()
    {
        _configService.EngineLegalNoticeAccepted = true;

        return TypedResults.NoContent();
    }
}
