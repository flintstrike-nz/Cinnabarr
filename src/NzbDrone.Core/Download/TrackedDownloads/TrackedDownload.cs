using System;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class TrackedDownload
    {
        public const string IncompleteLinkMessage = "Some episodes this torrent is linked to are no longer in the library, so it won't be imported automatically. Use Manual Import to link it.";
        public const string MissingFromEngineMessage = "Missing from engine: Embarr grabbed this torrent but it is no longer in the engine. Nothing has been deleted. Re-add it, or remove it from the queue to dismiss it.";
        public const string UnlinkedMessage = "Unlinked: this torrent wasn't added by Embarr, so it won't be imported automatically. Use Manual Import to link it.";

        public int DownloadClient { get; set; }
        public DownloadClientItem DownloadItem { get; set; }
        public DownloadClientItem ImportItem { get; set; }
        public TrackedDownloadState State { get; set; }
        public TrackedDownloadStatus Status { get; private set; }
        public RemoteEpisode RemoteEpisode { get; set; }

        // Set instead of RemoteEpisode when the download is for a movie.
        public RemoteMovie RemoteMovie { get; set; }
        public TrackedDownloadStatusMessage[] StatusMessages { get; private set; }
        public DownloadProtocol Protocol { get; set; }
        public string Indexer { get; set; }
        public DateTime? Added { get; set; }
        public bool IsTrackable { get; set; }

        // A built-in engine torrent with no grab record and no link sidecar (roadmap §4.4).
        public bool IsUnlinked { get; set; }

        // A grab sent to the built-in engine whose torrent is no longer there (roadmap §4.4 rule 3).
        // Listed for visibility only: never imported or failed automatically.
        public bool IsMissingFromEngine { get; set; }

        // A built-in engine torrent whose recorded series or episodes no longer all exist.
        public bool HasIncompleteLink { get; set; }
        public bool HasNotifiedManualInteractionRequired { get; set; }

        public TrackedDownload()
        {
            StatusMessages = Array.Empty<TrackedDownloadStatusMessage>();
        }

        public void Warn(string message, params object[] args)
        {
            var statusMessage = string.Format(message, args);
            Warn(new TrackedDownloadStatusMessage(DownloadItem.Title, statusMessage));
        }

        public void Warn(params TrackedDownloadStatusMessage[] statusMessages)
        {
            Status = TrackedDownloadStatus.Warning;
            StatusMessages = statusMessages;
        }

        public void Fail()
        {
            Status = TrackedDownloadStatus.Error;
            State = TrackedDownloadState.FailedPending;

            // Set CanBeRemoved to allow the failed item to be removed from the client
            DownloadItem.CanBeRemoved = true;
        }
    }

    public enum TrackedDownloadState
    {
        Downloading,
        ImportBlocked,
        ImportPending,
        Importing,
        Imported,
        FailedPending,
        Failed,
        Ignored
    }

    public enum TrackedDownloadStatus
    {
        Ok,
        Warning,
        Error
    }
}
