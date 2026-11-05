using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.IndexerEngine.Indexers.Exceptions
{
    public class IndexerAuthException : NzbDroneException
    {
        public IndexerAuthException(string message, params object[] args)
            : base(message, args)
        {
        }

        public IndexerAuthException(string message)
            : base(message)
        {
        }
    }
}
