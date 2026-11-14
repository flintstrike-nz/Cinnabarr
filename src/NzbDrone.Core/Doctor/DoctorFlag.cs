using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Doctor
{
    public enum DoctorMediaType
    {
        Series = 1,
        Movie = 2
    }

    public enum DoctorFlagState
    {
        // Waiting for review; deleted once it is older than the retention period.
        Flagged = 0,

        // The user decided to keep it. It is not flagged again.
        Recovered = 1
    }

    /// <summary>A file Doctor thinks is a worse copy of another file for the same episode or movie.</summary>
    public class DoctorFlag : ModelBase
    {
        public DoctorMediaType MediaType { get; set; }

        // The series or movie the file belongs to.
        public int ItemId { get; set; }

        // Episode file id or movie file id, by MediaType.
        public int FileId { get; set; }

        // The copy Doctor kept instead.
        public int KeptFileId { get; set; }

        // Why this copy is the worse one, e.g. "Lower bitrate than the kept copy".
        public string Reason { get; set; }

        public DoctorFlagState State { get; set; }
        public DateTime FlaggedAt { get; set; }
    }
}
