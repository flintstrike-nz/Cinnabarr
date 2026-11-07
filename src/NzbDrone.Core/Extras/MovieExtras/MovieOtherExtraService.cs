using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Extras.MovieExtras
{
    // .nfo, artwork and anything else that comes with a movie file; subtitles are left to MovieSubtitleService.
    public class MovieOtherExtraService : MovieExtraFileManager<MovieOtherExtraFile>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IMovieOtherExtraFileService _otherExtraFileService;
        private readonly IMediaFileAttributeService _mediaFileAttributeService;
        private readonly Logger _logger;

        public MovieOtherExtraService(IConfigService configService,
                                      IDiskProvider diskProvider,
                                      IDiskTransferService diskTransferService,
                                      IMovieOtherExtraFileService otherExtraFileService,
                                      IMediaFileAttributeService mediaFileAttributeService,
                                      Logger logger)
            : base(configService, diskProvider, diskTransferService, logger)
        {
            _diskProvider = diskProvider;
            _otherExtraFileService = otherExtraFileService;
            _mediaFileAttributeService = mediaFileAttributeService;
            _logger = logger;
        }

        public override int Order => 2;

        public override IEnumerable<MovieExtraFile> CreateAfterMediaCoverUpdate(Movie movie)
        {
            return Enumerable.Empty<MovieExtraFile>();
        }

        public override IEnumerable<MovieExtraFile> CreateAfterMovieScan(Movie movie, List<MovieFile> movieFiles)
        {
            return Enumerable.Empty<MovieExtraFile>();
        }

        public override IEnumerable<MovieExtraFile> CreateAfterMovieImport(Movie movie, MovieFile movieFile)
        {
            return Enumerable.Empty<MovieExtraFile>();
        }

        public override IEnumerable<MovieExtraFile> CreateAfterMovieFolder(Movie movie, string movieFolder)
        {
            return Enumerable.Empty<MovieExtraFile>();
        }

        public override IEnumerable<MovieExtraFile> MoveFilesAfterRename(Movie movie, List<MovieFile> movieFiles)
        {
            var extraFiles = _otherExtraFileService.GetFilesByMovie(movie.Id);
            var movedFiles = new List<MovieOtherExtraFile>();

            foreach (var movieFile in movieFiles)
            {
                var extraFilesForMovieFile = extraFiles.Where(m => m.MovieFileId == movieFile.Id).ToList();

                foreach (var extraFile in extraFilesForMovieFile)
                {
                    movedFiles.AddIfNotNull(MoveFile(movie, movieFile, extraFile));
                }
            }

            _otherExtraFileService.Upsert(movedFiles);

            return movedFiles;
        }

        public override bool CanImportFile(LocalMovie localMovie, MovieFile movieFile, string path, string extension, bool readOnly)
        {
            return !SubtitleFileExtensions.Extensions.Contains(extension.ToLowerInvariant());
        }

        public override IEnumerable<MovieExtraFile> ImportFiles(LocalMovie localMovie, MovieFile movieFile, List<string> files, bool isReadOnly)
        {
            var importedFiles = new List<MovieExtraFile>();
            var sourceFileName = Path.GetFileNameWithoutExtension(localMovie.Path);
            var matchingFiles = new List<string>();
            var hasNfo = false;

            foreach (var file in files)
            {
                // Filter out duplicate NFO files
                if (file.EndsWith(".nfo", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (hasNfo)
                    {
                        continue;
                    }

                    hasNfo = true;
                }

                if (Path.GetFileNameWithoutExtension(file).StartsWith(sourceFileName, StringComparison.InvariantCultureIgnoreCase))
                {
                    matchingFiles.Add(file);
                }
            }

            var copy = 1;

            foreach (var file in matchingFiles)
            {
                try
                {
                    var suffix = matchingFiles.Count > 1 ? new StringBuilder().Append('.').Append(copy).ToString() : string.Empty;
                    var extraFile = ImportFile(localMovie.Movie, movieFile, file, isReadOnly, Path.GetExtension(file), suffix);

                    if (extraFile == null)
                    {
                        continue;
                    }

                    _mediaFileAttributeService.SetFilePermissions(file);
                    _otherExtraFileService.Upsert(extraFile);
                    importedFiles.Add(extraFile);

                    copy++;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to import extra file: {0}", file);
                }
            }

            return importedFiles;
        }
    }
}
