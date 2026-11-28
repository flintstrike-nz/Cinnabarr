using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Operations
{
    // How a file's operations are titled in the queue.
    public static class FileOperationTitles
    {
        public static string Episode(Series series, IEnumerable<Episode> episodes)
        {
            var list = episodes?.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList() ?? new List<Episode>();

            if (!list.Any())
            {
                return series?.Title;
            }

            var numbers = string.Join("-", list.Select(e => $"E{e.EpisodeNumber:00}"));

            return $"{series?.Title} S{list.First().SeasonNumber:00}{numbers}";
        }

        public static string Movie(Movie movie)
        {
            return movie == null ? null : movie.Year > 0 ? $"{movie.Title} ({movie.Year})" : movie.Title;
        }
    }

    public enum FileOperationStatus
    {
        Queued,
        Running,
        Completed,
        Skipped,
        Failed
    }

    public static class FileOperationKinds
    {
        public const string Import = "import";
        public const string Move = "move";
        public const string Copy = "copy";
        public const string Rename = "rename";
    }

    // One step done to one file: importing it, or moving, copying or renaming it. Kept in memory only.
    public class FileOperation : ModelBase
    {
        // The order the operations run in: queued ones run before ones queued after them.
        public long RunIndex { get; set; }
        public string Kind { get; set; }
        public string Title { get; set; }
        public string Path { get; set; }
        public string Destination { get; set; }
        public FileOperationStatus Status { get; set; }

        // 0 to 100. Moves across drives and copies report how much of the file has arrived; other steps jump to 100.
        public double Progress { get; set; }
        public long Size { get; set; }

        // Why it was skipped or failed, or a note on how it went.
        public string Message { get; set; }
        public DateTime Queued { get; set; }
        public DateTime? Started { get; set; }
        public DateTime? Ended { get; set; }

        public bool IsFinished => Status is FileOperationStatus.Completed or FileOperationStatus.Skipped or FileOperationStatus.Failed;

        public FileOperation Clone()
        {
            return (FileOperation)MemberwiseClone();
        }
    }

    public class FileOperationUpdatedEvent : IEvent
    {
        public FileOperation Operation { get; }

        public FileOperationUpdatedEvent(FileOperation operation)
        {
            Operation = operation;
        }
    }

    public class FileOperationsClearedEvent : IEvent
    {
    }

    public interface IFileOperationTracker
    {
        // Adds a step to the queue, to be started later.
        FileOperation Queue(string kind, string title, string path, string destination = null);

        void Start(FileOperation operation);
        void Complete(FileOperation operation, string message = null);
        void Skip(FileOperation operation, string reason);
        void Fail(FileOperation operation, string reason);

        // Starts moving, copying or renaming a file as one step, and reports how far a copy has got until the step is
        // completed or failed. It uses the step queued for the file when there is one (so it keeps its place in the
        // queue) and adds one when there isn't.
        FileOperation StartTransfer(string title, string source, string destination, TransferMode mode);

        // Fails a step with the reason an exception gives, in words for the user.
        void Fail(FileOperation operation, Exception exception);

        // Ends the steps still queued for a file (ones that never ran because an earlier step stopped).
        void SkipQueued(string path, string reason);

        List<FileOperation> All();
        FileOperation Get(int id);
        void ClearFinished();
    }

    /// <summary>
    /// The queue of file operations the operations panel shows: one row per file per step (import x, then move x),
    /// in the order they run, with each step's progress and, when it doesn't happen, why. Torrent downloads aren't
    /// file operations; the Downloads page shows those.
    /// </summary>
    public class FileOperationTracker : IFileOperationTracker
    {
        private const int KeepFinished = 500;
        private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(500);

        private readonly IEventAggregator _eventAggregator;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;
        private readonly List<FileOperation> _operations = new();
        private readonly Dictionary<int, Timer> _samplers = new();
        private readonly object _lock = new();
        private int _nextId;
        private long _nextRunIndex;

        public FileOperationTracker(IEventAggregator eventAggregator, IDiskProvider diskProvider, Logger logger)
        {
            _eventAggregator = eventAggregator;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public FileOperation Queue(string kind, string title, string path, string destination = null)
        {
            var operation = new FileOperation
            {
                Kind = kind,
                Title = title,
                Path = path,
                Destination = destination,
                Status = FileOperationStatus.Queued,
                Queued = DateTime.UtcNow
            };

            lock (_lock)
            {
                operation.Id = ++_nextId;
                operation.RunIndex = ++_nextRunIndex;
                _operations.Add(operation);
                Trim();
            }

            Publish(operation);

            return operation;
        }

        public void Start(FileOperation operation)
        {
            if (operation == null)
            {
                return;
            }

            Update(operation, o =>
            {
                o.Status = FileOperationStatus.Running;
                o.Started = DateTime.UtcNow;
            });
        }

        public void Complete(FileOperation operation, string message = null)
        {
            Finish(operation, FileOperationStatus.Completed, message);
        }

        public void Skip(FileOperation operation, string reason)
        {
            Finish(operation, FileOperationStatus.Skipped, reason);
        }

        public void Fail(FileOperation operation, string reason)
        {
            Finish(operation, FileOperationStatus.Failed, reason);
        }

        public FileOperation StartTransfer(string title, string source, string destination, TransferMode mode)
        {
            var kind = KindOf(source, destination, mode);
            var operation = Claim(source) ?? Queue(kind, title, source, destination);

            Update(operation, o =>
            {
                o.Kind = kind;
                o.Title ??= title;
                o.Destination = destination;
                o.Size = SizeOf(source);
            });

            Start(operation);

            if (operation.Size > 0)
            {
                lock (_lock)
                {
                    _samplers[operation.Id] = new Timer(_ => Sample(operation), null, SampleInterval, SampleInterval);
                }
            }

            return operation;
        }

        public void Fail(FileOperation operation, Exception exception)
        {
            switch (exception)
            {
                case FileAlreadyExistsException:
                case DestinationAlreadyExistsException:
                    Skip(operation, $"There's already a file named {System.IO.Path.GetFileName(operation?.Destination)}");
                    break;
                case SameFilenameException:
                    Skip(operation, "It already has that name");
                    break;
                case FileNotFoundException:
                    Fail(operation, "The file isn't there any more");
                    break;
                case UnauthorizedAccessException:
                    Fail(operation, "Cinnabarr isn't allowed to change it (permissions)");
                    break;
                default:
                    Fail(operation, exception.Message);
                    break;
            }
        }

        public void SkipQueued(string path, string reason)
        {
            List<FileOperation> queued;

            lock (_lock)
            {
                queued = _operations.Where(o => o.Status == FileOperationStatus.Queued && o.Path.PathEquals(path)).ToList();
            }

            foreach (var operation in queued)
            {
                Skip(operation, reason);
            }
        }

        public List<FileOperation> All()
        {
            lock (_lock)
            {
                return _operations.OrderBy(o => o.RunIndex).Select(o => o.Clone()).ToList();
            }
        }

        public FileOperation Get(int id)
        {
            lock (_lock)
            {
                return _operations.SingleOrDefault(o => o.Id == id)?.Clone();
            }
        }

        public void ClearFinished()
        {
            lock (_lock)
            {
                _operations.RemoveAll(o => o.IsFinished);
            }

            _eventAggregator.PublishEvent(new FileOperationsClearedEvent());
        }

        private static string KindOf(string source, string destination, TransferMode mode)
        {
            if (mode is TransferMode.Copy or TransferMode.HardLinkOrCopy or TransferMode.HardLink)
            {
                return FileOperationKinds.Copy;
            }

            var sourceFolder = System.IO.Path.GetDirectoryName(source);
            var destinationFolder = System.IO.Path.GetDirectoryName(destination);

            return sourceFolder.IsNotNullOrWhiteSpace() && destinationFolder.IsNotNullOrWhiteSpace() && sourceFolder.PathEquals(destinationFolder)
                ? FileOperationKinds.Rename
                : FileOperationKinds.Move;
        }

        // The step queued for a file by whatever is working through a list of them.
        private FileOperation Claim(string path)
        {
            lock (_lock)
            {
                return _operations.FirstOrDefault(o => o.Status == FileOperationStatus.Queued &&
                                                       o.Kind != FileOperationKinds.Import &&
                                                       o.Path.PathEquals(path));
            }
        }

        private long SizeOf(string path)
        {
            try
            {
                return _diskProvider.GetFileSize(path);
            }
            catch (Exception ex)
            {
                _logger.Trace(ex, "Unable to get the size of '{0}'", path);

                return 0;
            }
        }

        // How much of the file has reached its destination. A move on one drive is a rename and never shows partway.
        private void Sample(FileOperation operation)
        {
            if (operation.Status != FileOperationStatus.Running || operation.Destination == null)
            {
                return;
            }

            try
            {
                if (!_diskProvider.FileExists(operation.Destination))
                {
                    return;
                }

                var arrived = _diskProvider.GetFileSize(operation.Destination);
                var progress = Math.Min(99, Math.Round(arrived * 100.0 / operation.Size, 1));

                if (progress > operation.Progress)
                {
                    Update(operation, o => o.Progress = progress);
                }
            }
            catch (Exception ex)
            {
                _logger.Trace(ex, "Unable to measure '{0}'", operation.Destination);
            }
        }

        private void Finish(FileOperation operation, FileOperationStatus status, string message)
        {
            if (operation == null)
            {
                return;
            }

            lock (_lock)
            {
                if (_samplers.Remove(operation.Id, out var sampler))
                {
                    sampler.Dispose();
                }
            }

            Update(operation, o =>
            {
                if (o.IsFinished)
                {
                    return;
                }

                o.Status = status;
                o.Message = message;
                o.Ended = DateTime.UtcNow;
                o.Started ??= o.Ended;

                if (status == FileOperationStatus.Completed)
                {
                    o.Progress = 100;
                }
            });
        }

        private void Update(FileOperation operation, Action<FileOperation> change)
        {
            lock (_lock)
            {
                change(operation);
            }

            Publish(operation);
        }

        private void Publish(FileOperation operation)
        {
            _eventAggregator.PublishEvent(new FileOperationUpdatedEvent(operation.Clone()));
        }

        // Only finished steps are dropped, oldest first, so the queue never loses something still to happen.
        private void Trim()
        {
            var finished = _operations.Where(o => o.IsFinished).ToList();

            if (finished.Count > KeepFinished)
            {
                foreach (var operation in finished.OrderBy(o => o.RunIndex).Take(finished.Count - KeepFinished))
                {
                    _operations.Remove(operation);
                }
            }
        }
    }
}
