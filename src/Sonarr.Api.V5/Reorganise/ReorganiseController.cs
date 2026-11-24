using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Reorganise;
using Sonarr.Http;

namespace Sonarr.Api.V5.Reorganise;

public class ReorganiseEntryResource
{
    public string Kind { get; set; } = ReorganiseKinds.Rename;
    public required string MediaType { get; set; }
    public int MediaId { get; set; }
    public string? Title { get; set; }
    public int FileId { get; set; }
    public required string From { get; set; }
    public string? To { get; set; }

    // Why the item won't be done; null when it will be.
    public string? Problem { get; set; }
    public DateTime? Undone { get; set; }
}

// The library reorganise tool (roadmap §4.12): what a run would do (in every root folder, or in the folder given), and
// the runs that were done. Doing a run, and undoing one, are the ReorganiseLibrary and ReorganiseUndo commands.
[V5ApiController("reorganise")]
public class ReorganiseController : Controller
{
    private readonly IReorganiseService _reorganiseService;

    public ReorganiseController(IReorganiseService reorganiseService)
    {
        _reorganiseService = reorganiseService;
    }

    [HttpGet("preview")]
    [Produces("application/json")]
    public Ok<List<ReorganiseEntryResource>> Preview([FromQuery] string? folder = null, [FromQuery] bool includeFolders = false)
    {
        return TypedResults.Ok(_reorganiseService.Preview(folder, includeFolders).Select(p => new ReorganiseEntryResource
        {
            Kind = p.Kind,
            MediaType = p.MediaType,
            MediaId = p.MediaId,
            Title = p.Title,
            FileId = p.FileId,
            From = p.From,
            To = p.To,
            Problem = p.Problem
        }).ToList());
    }

    [HttpGet("runs")]
    [Produces("application/json")]
    public Ok<List<ReorganiseRun>> GetRuns()
    {
        return TypedResults.Ok(_reorganiseService.GetRuns());
    }

    [HttpGet("runs/{runId}")]
    [Produces("application/json")]
    public Ok<List<ReorganiseEntryResource>> GetRun([FromRoute] string runId)
    {
        return TypedResults.Ok(_reorganiseService.GetRun(runId).Select(e => new ReorganiseEntryResource
        {
            MediaType = e.MediaType,
            MediaId = e.MediaId,
            Title = e.Title,
            FileId = e.FileId,
            From = e.FromPath,
            To = e.ToPath,
            Undone = e.Undone
        }).ToList());
    }
}
