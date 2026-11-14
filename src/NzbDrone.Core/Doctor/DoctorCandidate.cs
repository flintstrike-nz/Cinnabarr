using System;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Doctor
{
    /// <summary>One file Doctor looks at, with what it needs to compare copies of the same thing.</summary>
    public class DoctorCandidate
    {
        public DoctorMediaType MediaType { get; set; }
        public int ItemId { get; set; }

        // What the file is a copy of: the episode ids it holds, or "movie".
        public string GroupKey { get; set; }

        public int FileId { get; set; }
        public long Size { get; set; }
        public DateTime DateAdded { get; set; }
        public QualityModel Quality { get; set; }
        public MediaInfoModel MediaInfo { get; set; }

        // The user chose to keep this copy: it is never flagged, and it wins over the others.
        public bool IsProtected { get; set; }
    }

    public class DoctorDuplicate
    {
        public DoctorCandidate Worse { get; set; }
        public DoctorCandidate Better { get; set; }
        public string Reason { get; set; }
    }
}
