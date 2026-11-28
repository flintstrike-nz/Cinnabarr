using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Operations;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;
using Debouncer = NzbDrone.Common.TPL.Debouncer;

namespace Sonarr.Api.V5.FileOperations;

public class FileOperationResource : RestResource
{
    public long RunIndex { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? Destination { get; set; }
    public FileOperationStatus Status { get; set; }
    public double Progress { get; set; }
    public long Size { get; set; }
    public string? Message { get; set; }
    public DateTime Queued { get; set; }
    public DateTime? Started { get; set; }
    public DateTime? Ended { get; set; }
}

public static class FileOperationResourceMapper
{
    public static FileOperationResource ToResource(this FileOperation model)
    {
        return new FileOperationResource
        {
            Id = model.Id,
            RunIndex = model.RunIndex,
            Kind = model.Kind,
            Title = model.Title,
            Path = model.Path,
            Destination = model.Destination,
            Status = model.Status,
            Progress = model.Progress,
            Size = model.Size,
            Message = model.Message,
            Queued = model.Queued,
            Started = model.Started,
            Ended = model.Ended
        };
    }
}

// The operations queue: one row per file per step (import, move, copy, rename), in the order they run. Changes are
// pushed over SignalR as they happen, a few times a second at most.
[V5ApiController("fileoperation")]
public class FileOperationController : RestControllerWithSignalR<FileOperationResource, FileOperation>,
                                       IHandle<FileOperationUpdatedEvent>,
                                       IHandle<FileOperationsClearedEvent>
{
    private readonly IFileOperationTracker _tracker;
    private readonly Debouncer _debouncer;
    private readonly Dictionary<int, FileOperationResource> _pendingUpdates = new();

    public FileOperationController(IFileOperationTracker tracker, IBroadcastSignalRMessage signalRBroadcaster)
        : base(signalRBroadcaster)
    {
        _tracker = tracker;
        _debouncer = new Debouncer(SendUpdates, TimeSpan.FromSeconds(0.25));
    }

    protected override FileOperationResource GetResourceById(int id)
    {
        var operation = _tracker.Get(id);

        if (operation == null)
        {
            throw new NzbDrone.Core.Datastore.ModelNotFoundException(typeof(FileOperation), id);
        }

        return operation.ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<FileOperationResource>> GetOperations()
    {
        return TypedResults.Ok(_tracker.All().Select(o => o.ToResource()).ToList());
    }

    // Removes the finished rows; queued and running ones stay.
    [HttpDelete("finished")]
    public NoContent ClearFinished()
    {
        _tracker.ClearFinished();

        return TypedResults.NoContent();
    }

    [NonAction]
    public void Handle(FileOperationUpdatedEvent message)
    {
        lock (_pendingUpdates)
        {
            _pendingUpdates[message.Operation.Id] = message.Operation.ToResource();
        }

        _debouncer.Execute();
    }

    [NonAction]
    public void Handle(FileOperationsClearedEvent message)
    {
        BroadcastResourceChange(ModelAction.Sync);
    }

    private void SendUpdates()
    {
        FileOperationResource[] updates;

        lock (_pendingUpdates)
        {
            updates = _pendingUpdates.Values.OrderBy(u => u.RunIndex).ToArray();
            _pendingUpdates.Clear();
        }

        foreach (var update in updates)
        {
            BroadcastResourceChange(ModelAction.Updated, update);
        }
    }
}
