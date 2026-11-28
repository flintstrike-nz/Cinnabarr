using System;
using System.Collections.Generic;
using System.IO;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaFiles
{
    /// <summary>
    /// Where a file goes when its destination already holds another file: the same name with a number added,
    /// "Name (2).mkv", "Name (3).mkv" and so on. The file is never left behind or written over; Doctor then compares
    /// the copies and flags the worse one.
    /// </summary>
    public static class FreeFilePath
    {
        private const int MaxNumber = 1000;

        /// <summary>
        /// The destination, or the first numbered name that is free. The file's own current path counts as free, so a
        /// file already numbered keeps its name, and a change of case only is not a clash. Paths in taken (names other
        /// files of the same run will get) count as used.
        /// </summary>
        public static string Find(IDiskProvider diskProvider, string destination, string source = null, ICollection<string> taken = null)
        {
            if (IsFree(diskProvider, destination, source, taken))
            {
                return destination;
            }

            var folder = Path.GetDirectoryName(destination);
            var name = Path.GetFileNameWithoutExtension(destination);
            var extension = Path.GetExtension(destination);

            for (var number = 2; number <= MaxNumber; number++)
            {
                var candidate = Path.Combine(folder, $"{name} ({number}){extension}");

                if (IsFree(diskProvider, candidate, source, taken))
                {
                    return candidate;
                }
            }

            throw new DestinationAlreadyExistsException($"Destination {destination} and its numbered names up to ({MaxNumber}) already exist.");
        }

        /// <summary>
        /// Whether the file has its right name already, with a number because another file holds the name. It is
        /// renamed again only once that other file is gone.
        /// </summary>
        public static bool IsNumberedCopy(IDiskProvider diskProvider, string path, string destination)
        {
            if (!Path.GetDirectoryName(path).PathEquals(Path.GetDirectoryName(destination)) ||
                !Path.GetExtension(path).Equals(Path.GetExtension(destination), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var name = Path.GetFileNameWithoutExtension(destination);
            var pathName = Path.GetFileNameWithoutExtension(path);

            if (!pathName.StartsWith(name + " (", StringComparison.Ordinal) || !pathName.EndsWith(")", StringComparison.Ordinal))
            {
                return false;
            }

            var number = pathName.Substring(name.Length + 2, pathName.Length - name.Length - 3);

            return int.TryParse(number, out var n) && n >= 2 && number == n.ToString() && diskProvider.FileExists(destination);
        }

        private static bool IsFree(IDiskProvider diskProvider, string path, string source, ICollection<string> taken)
        {
            if (taken != null && taken.Contains(path))
            {
                return false;
            }

            return (source != null && path.PathEquals(source, StringComparison.OrdinalIgnoreCase)) || !diskProvider.FileExists(path);
        }
    }
}
