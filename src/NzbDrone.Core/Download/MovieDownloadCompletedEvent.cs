using NzbDrone.Common.Messaging;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public class MovieDownloadCompletedEvent : IEvent
    {
        public TrackedDownload TrackedDownload { get; private set; }
        public Movie Movie { get; private set; }
        public MovieFile MovieFile { get; private set; }
        public GrabbedReleaseInfo Release { get; private set; }

        public MovieDownloadCompletedEvent(TrackedDownload trackedDownload, Movie movie, MovieFile movieFile, GrabbedReleaseInfo release)
        {
            TrackedDownload = trackedDownload;
            Movie = movie;
            MovieFile = movieFile;
            Release = release;
        }
    }
}
