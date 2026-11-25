using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Movies
{
    public class UnmappedMovieFolder
    {
        public string Name { get; set; }
        public string Path { get; set; }

        // A video file lying loose in the root folder rather than a folder.
        public bool IsFile { get; set; }

        // What the folder name looks like: the title and year to look the movie up with.
        public string ParsedTitle { get; set; }
        public int ParsedYear { get; set; }
    }

    public interface IUnmappedMovieFolderService
    {
        // The folders directly inside a folder that no movie in the library points at, and the video files lying
        // loose in it (library import).
        List<UnmappedMovieFolder> Get(string path);
    }

    public class UnmappedMovieFolderService : IUnmappedMovieFolderService
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IMovieService _movieService;

        public UnmappedMovieFolderService(IDiskProvider diskProvider, IMovieService movieService)
        {
            _diskProvider = diskProvider;
            _movieService = movieService;
        }

        public List<UnmappedMovieFolder> Get(string path)
        {
            if (path.IsNullOrWhiteSpace() || !_diskProvider.FolderExists(path))
            {
                return new List<UnmappedMovieFolder>();
            }

            var known = _movieService.AllMoviePaths().Values;

            var folders = _diskProvider.GetDirectories(path)
                .Where(f => !Path.GetFileName(f.TrimEnd(Path.DirectorySeparatorChar)).StartsWith("."))
                .Except(known, PathEqualityComparer.Instance)
                .Select(folder =>
                {
                    var name = new DirectoryInfo(folder).Name;

                    return Describe(name, name, folder, false);
                });

            // Video files lying loose in the root folder: each is a movie of its own, without a folder yet.
            var files = (_diskProvider.GetFiles(path, false) ?? Enumerable.Empty<string>())
                .Where(f => !Path.GetFileName(f).StartsWith(".") && MediaFileExtensions.Extensions.Contains(Path.GetExtension(f)))
                .Select(file => Describe(Path.GetFileName(file), Path.GetFileNameWithoutExtension(file), file, true));

            return folders.Concat(files)
                .OrderBy(f => f.Name, StringComparer.InvariantCultureIgnoreCase)
                .ToList();
        }

        private static UnmappedMovieFolder Describe(string name, string titleSource, string path, bool isFile)
        {
            var parsed = MovieParser.ParseMovieTitle(titleSource, true);

            return new UnmappedMovieFolder
            {
                Name = name,
                Path = path,
                IsFile = isFile,
                ParsedTitle = parsed?.PrimaryMovieTitle ?? titleSource,
                ParsedYear = parsed?.Year ?? 0
            };
        }
    }
}
