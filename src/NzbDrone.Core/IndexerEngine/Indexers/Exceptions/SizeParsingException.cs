using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.IndexerEngine.Indexers.Exceptions
{
    public class SizeParsingException : NzbDroneException
    {
        public SizeParsingException(string message, params object[] args)
            : base(message, args)
        {
        }
    }
}
