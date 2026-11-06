using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine
{
    public class DownloadDecision
    {
        public RemoteEpisode RemoteEpisode { get; private set; }

        // Set instead of RemoteEpisode when the release is for a movie.
        public RemoteMovie RemoteMovie { get; private set; }

        // Whichever of the two the release was matched to.
        public RemoteMedia Remote => (RemoteMedia)RemoteEpisode ?? RemoteMovie;

        public IEnumerable<DownloadRejection> Rejections { get; private set; }

        public bool Approved => !Rejections.Any();

        public bool TemporarilyRejected
        {
            get
            {
                return Rejections.Any() && Rejections.All(r => r.Type == RejectionType.Temporary);
            }
        }

        public bool Rejected
        {
            get
            {
                return Rejections.Any() && Rejections.Any(r => r.Type == RejectionType.Permanent);
            }
        }

        public DownloadDecision(RemoteEpisode episode, params DownloadRejection[] rejections)
        {
            RemoteEpisode = episode;
            Rejections = rejections.ToList();
        }

        public DownloadDecision(RemoteMovie movie, params DownloadRejection[] rejections)
        {
            RemoteMovie = movie;
            Rejections = rejections.ToList();
        }

        public override string ToString()
        {
            object release = RemoteEpisode != null ? RemoteEpisode : RemoteMovie;

            if (Approved)
            {
                return "[OK] " + release;
            }

            return "[Rejected " + Rejections.Count() + "]" + release;
        }
    }
}
