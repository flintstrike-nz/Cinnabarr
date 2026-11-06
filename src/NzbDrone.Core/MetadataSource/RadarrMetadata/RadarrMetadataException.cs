using System;
using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.MetadataSource.RadarrMetadata
{
    public class RadarrMetadataException : NzbDroneException
    {
        public RadarrMetadataException(string message, params object[] args)
            : base(message, args)
        {
        }

        public RadarrMetadataException(string message, Exception innerException, params object[] args)
            : base(message, innerException, args)
        {
        }
    }
}
