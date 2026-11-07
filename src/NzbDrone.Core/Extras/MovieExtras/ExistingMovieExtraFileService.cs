using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation.Aggregators;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Extras.MovieExtras
{
    // When a movie folder is scanned, link the subtitles, .nfo and artwork already sitting beside the movie's files.
    // A file belongs to a movie file when it is in the same folder and its name starts with that file's name; a subtitle
    // that matches nothing is given to the movie's only file.
    public class ExistingMovieExtraFileService : IHandle<MovieScannedEvent>
    {
        private static readonly string[] IgnoredExtensions = { ".!qb", ".parts", ".part", ".tmp", ".partial" };

        private readonly IMovieFileService _movieFileService;
        private readonly IMovieSubtitleFileService _subtitleFileService;
        private readonly IMovieOtherExtraFileService _otherExtraFileService;
        private readonly Logger _logger;

        public ExistingMovieExtraFileService(IMovieFileService movieFileService,
                                             IMovieSubtitleFileService subtitleFileService,
                                             IMovieOtherExtraFileService otherExtraFileService,
                                             Logger logger)
        {
            _movieFileService = movieFileService;
            _subtitleFileService = subtitleFileService;
            _otherExtraFileService = otherExtraFileService;
            _logger = logger;
        }

        public void Handle(MovieScannedEvent message)
        {
            var movie = message.Movie;
            var filesOnDisk = message.PossibleExtraFiles ?? new List<string>();
            var movieFiles = _movieFileService.GetFilesByMovie(movie.Id);

            var knownSubtitles = _subtitleFileService.GetFilesByMovie(movie.Id);
            var knownOthers = _otherExtraFileService.GetFilesByMovie(movie.Id);

            // Forget the records of files that are gone, or whose movie file is gone.
            _subtitleFileService.DeleteMany(knownSubtitles.Where(f => IsStale(movie, f, filesOnDisk, movieFiles)).Select(f => f.Id));
            _otherExtraFileService.DeleteMany(knownOthers.Where(f => IsStale(movie, f, filesOnDisk, movieFiles)).Select(f => f.Id));

            if (movieFiles.Empty())
            {
                return;
            }

            var tracked = knownSubtitles.Cast<MovieExtraFile>()
                                        .Concat(knownOthers)
                                        .Select(f => Path.Combine(movie.Path, f.RelativePath))
                                        .ToList();

            var newFiles = filesOnDisk.Except(tracked, PathEqualityComparer.Instance)
                                      .Where(f => !IgnoredExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                      .Where(f => Path.GetExtension(f).IsNotNullOrWhiteSpace())
                                      .ToList();

            var newSubtitles = new List<MovieSubtitleFile>();
            var newOthers = new List<MovieOtherExtraFile>();

            foreach (var file in newFiles)
            {
                var extension = Path.GetExtension(file);
                var isSubtitle = SubtitleFileExtensions.Extensions.Contains(extension.ToLowerInvariant());
                var movieFile = FindMovieFile(movie, movieFiles, file, isSubtitle);

                if (movieFile == null)
                {
                    _logger.Debug("Cannot find the movie file for: {0}", file);
                    continue;
                }

                if (isSubtitle)
                {
                    var info = new AggregateSubtitleInfo(_logger).CleanSubtitleTitleInfo(movieFile, file, null);

                    newSubtitles.Add(new MovieSubtitleFile
                    {
                        MovieId = movie.Id,
                        MovieFileId = movieFile.Id,
                        RelativePath = movie.Path.GetRelativePath(file),
                        Language = info?.Language ?? Language.Unknown,
                        LanguageTags = info?.LanguageTags ?? new List<string>(),
                        Title = info?.Title,
                        Extension = extension,
                        Copy = info?.Copy ?? 0
                    });
                }
                else
                {
                    newOthers.Add(new MovieOtherExtraFile
                    {
                        MovieId = movie.Id,
                        MovieFileId = movieFile.Id,
                        RelativePath = movie.Path.GetRelativePath(file),
                        Extension = extension
                    });
                }
            }

            _subtitleFileService.Upsert(newSubtitles);
            _otherExtraFileService.Upsert(newOthers);

            _logger.Info("Found {0} possible extra files for {1}, linked {2} subtitle and {3} other files.", filesOnDisk.Count, movie.Title, newSubtitles.Count, newOthers.Count);
        }

        private static bool IsStale(Movie movie, MovieExtraFile extra, List<string> filesOnDisk, List<MovieFile> movieFiles)
        {
            var path = Path.Combine(movie.Path, extra.RelativePath);

            return !filesOnDisk.Contains(path, PathEqualityComparer.Instance) ||
                   (extra.MovieFileId.HasValue && movieFiles.All(m => m.Id != extra.MovieFileId.Value));
        }

        private static MovieFile FindMovieFile(Movie movie, List<MovieFile> movieFiles, string file, bool isSubtitle)
        {
            var folder = Path.GetDirectoryName(file);
            var name = Path.GetFileNameWithoutExtension(file);

            var match = movieFiles
                .Select(m => new { File = m, Path = Path.Combine(movie.Path, m.RelativePath) })
                .Where(m => Path.GetDirectoryName(m.Path).PathEquals(folder))
                .Select(m => new { m.File, Name = Path.GetFileNameWithoutExtension(m.Path) })
                .Where(m => name.StartsWith(m.Name, StringComparison.InvariantCultureIgnoreCase))
                .OrderByDescending(m => m.Name.Length)
                .Select(m => m.File)
                .FirstOrDefault();

            if (match == null && isSubtitle && movieFiles.Count == 1)
            {
                return movieFiles[0];
            }

            return match;
        }
    }
}
