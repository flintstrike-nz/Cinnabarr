using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Requests;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Requests;

// "Add this item" from an outside client (the Jellyfin plugin, the remote request relay). Only mediaType and
// externalId are needed; the rest falls back to defaults. See docs/cinnabarr/JELLYFIN.md.
public class MediaRequestResource
{
    public RequestMediaType MediaType { get; set; }

    // TVDB id for a series, TMDb id for a movie.
    public int ExternalId { get; set; }
    public int? QualityProfileId { get; set; }
    public string? RootFolderPath { get; set; }
    public bool? SearchNow { get; set; }
    public NzbDrone.Core.Tv.MonitorTypes? SeriesMonitor { get; set; }
    public List<int>? Tags { get; set; }
}

public class MediaRequestResultResource
{
    public RequestMediaType MediaType { get; set; }
    public int ExternalId { get; set; }
    public int Id { get; set; }
    public string? Title { get; set; }
    public bool AlreadyExisted { get; set; }
}

public class LibraryItemStateResource
{
    public RequestMediaType MediaType { get; set; }
    public int ExternalId { get; set; }
    public int? Id { get; set; }
    public LibraryItemStatus Status { get; set; }
}

[V5ApiController("request")]
public class RequestController : Controller
{
    private readonly IMediaRequestService _requestService;
    private readonly ILibraryStatusService _statusService;

    public RequestController(IMediaRequestService requestService, ILibraryStatusService statusService)
    {
        _requestService = requestService;
        _statusService = statusService;
    }

    // Adds the series or movie with defaults and, unless searchNow is false, searches for it. Asking again for
    // something already in the library changes nothing and returns it with alreadyExisted set.
    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<MediaRequestResultResource> AddRequest([FromBody] MediaRequestResource resource)
    {
        if (resource == null)
        {
            throw new BadRequestException("A request body is required.");
        }

        var result = _requestService.Request(new MediaRequest
        {
            MediaType = resource.MediaType,
            ExternalId = resource.ExternalId,
            QualityProfileId = resource.QualityProfileId,
            RootFolderPath = resource.RootFolderPath,
            SearchNow = resource.SearchNow ?? true,
            SeriesMonitor = resource.SeriesMonitor,
            Tags = resource.Tags == null ? null : new HashSet<int>(resource.Tags)
        });

        return TypedResults.Ok(new MediaRequestResultResource
        {
            MediaType = result.MediaType,
            ExternalId = result.ExternalId,
            Id = result.Id,
            Title = result.Title,
            AlreadyExisted = result.AlreadyExisted
        });
    }

    // Whether items are in the library, by TVDB id (series) and TMDb id (movies), comma separated. At most 200 of each.
    [HttpGet("status")]
    [Produces("application/json")]
    public Ok<List<LibraryItemStateResource>> Status([FromQuery] string? tvdbIds = null, [FromQuery] string? tmdbIds = null)
    {
        var states = _statusService.GetStatus(ParseIds(tvdbIds), ParseIds(tmdbIds));

        return TypedResults.Ok(states.Select(s => new LibraryItemStateResource
        {
            MediaType = s.MediaType,
            ExternalId = s.ExternalId,
            Id = s.Id,
            Status = s.Status
        }).ToList());
    }

    private static List<int> ParseIds(string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
        {
            return [];
        }

        var result = new List<int>();

        foreach (var part in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var id) || id <= 0)
            {
                throw new BadRequestException($"'{part}' is not a valid id.");
            }

            result.Add(id);
        }

        return result;
    }
}
