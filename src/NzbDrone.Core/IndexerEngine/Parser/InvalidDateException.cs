using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.IndexerEngine.Parser
{
    public class InvalidDateException : NzbDroneException
    {
        public InvalidDateException(string message, params object[] args)
            : base(message, args)
        {
        }

        public InvalidDateException(string message)
            : base(message)
        {
        }
    }
}
