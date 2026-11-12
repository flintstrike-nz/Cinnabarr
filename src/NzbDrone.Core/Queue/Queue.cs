using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Queue
{
    public class Queue : ModelBase
    {
        public Series Series { get; set; }

        [Obsolete]
        public Episode Episode { get; set; }

        public List<Episode> Episodes { get; set; }

        // For a movie download; Series and Episodes are then empty.
        public Movie Movie { get; set; }
        public List<Language> Languages { get; set; }
        public QualityModel Quality { get; set; }
        public decimal Size { get; set; }
        public string Title { get; set; }
        public decimal SizeLeft { get; set; }
        public TimeSpan? TimeLeft { get; set; }
        public DateTime? EstimatedCompletionTime { get; set; }
        public DateTime? Added { get; set; }
        public QueueStatus Status { get; set; }
        public TrackedDownloadStatus? TrackedDownloadStatus { get; set; }
        public TrackedDownloadState? TrackedDownloadState { get; set; }

        // False for rows only listed for visibility (e.g. a built-in engine torrent that was
        // already imported and is seeding). Search and download decisions ignore those rows.
        public bool IsTrackable { get; set; } = true;
        public List<TrackedDownloadStatusMessage> StatusMessages { get; set; }
        public string DownloadId { get; set; }
        public RemoteEpisode RemoteEpisode { get; set; }
        public RemoteMovie RemoteMovie { get; set; }
        public DownloadProtocol Protocol { get; set; }
        public string DownloadClient { get; set; }
        public bool DownloadClientHasPostImportCategory { get; set; }
        public string Indexer { get; set; }
        public string OutputPath { get; set; }
        public string ErrorMessage { get; set; }

        // A download in the built-in engine (roadmap §6.2), and whether the engine no longer has it (§4.4).
        public bool IsEngineDownload { get; set; }
        public bool IsMissingFromEngine { get; set; }

        // In the engine with no Cinnabarr record (roadmap §4.4): not imported until Manual Import links it.
        public bool IsUnlinked { get; set; }
    }
}
