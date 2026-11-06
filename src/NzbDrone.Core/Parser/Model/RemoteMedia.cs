using System.Collections.Generic;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Languages;

namespace NzbDrone.Core.Parser.Model
{
    /// <summary>What a release was matched to, series or movie: the part the download clients and the grab path share.</summary>
    public abstract class RemoteMedia
    {
        protected RemoteMedia()
        {
            CustomFormats = new List<CustomFormat>();
            Languages = new List<Language>();
        }

        public ReleaseInfo Release { get; set; }
        public bool DownloadAllowed { get; set; }
        public TorrentSeedConfiguration SeedConfiguration { get; set; }
        public List<CustomFormat> CustomFormats { get; set; }
        public int CustomFormatScore { get; set; }
        public List<Language> Languages { get; set; }
        public ReleaseSourceType ReleaseSource { get; set; }

        // The tags of the series or movie the release is for.
        public abstract HashSet<int> Tags { get; }

        // Whether the content is recent, which download clients may prioritise.
        public abstract bool IsRecent { get; }

        public override string ToString()
        {
            return Release.Title;
        }
    }
}
