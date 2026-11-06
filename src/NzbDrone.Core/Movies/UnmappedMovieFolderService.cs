using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Movies
{
    public class UnmappedMovieFolder
    {
        public string Name { get; set; }
        public string Path { get; set; }

        // What the folder name looks like: the title and year to look the movie up with.
        public string ParsedTitle { get; set; }
        public int ParsedYear { get; set; }
    }

    public interface IUnmappedMovieFolderService
    {
        // The folders directly inside a folder that no movie in the library points at (library import).
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

            return _diskProvider.GetDirectories(path)
                .Where(f => !Path.GetFileName(f.TrimEnd(Path.DirectorySeparatorChar)).StartsWith("."))
                .Except(known, PathEqualityComparer.Instance)
                .Select(folder =>
                {
                    var name = new DirectoryInfo(folder).Name;
                    var parsed = MovieParser.ParseMovieTitle(name, true);

                    return new UnmappedMovieFolder
                    {
                        Name = name,
                        Path = folder,
                        ParsedTitle = parsed?.PrimaryMovieTitle ?? name,
                        ParsedYear = parsed?.Year ?? 0
                    };
                })
                .OrderBy(f => f.Name, StringComparer.InvariantCultureIgnoreCase)
                .ToList();
        }
    }
}
