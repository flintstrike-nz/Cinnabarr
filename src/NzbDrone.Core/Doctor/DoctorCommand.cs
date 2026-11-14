using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Doctor
{
    /// <summary>Checks the library for worse duplicate copies. A scheduled run also deletes flagged files that have waited long enough.</summary>
    public class DoctorCommand : Command
    {
        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
    }
}
