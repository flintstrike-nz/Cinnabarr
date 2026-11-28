using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport.Manual;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tv;
using EpisodeImportMode = NzbDrone.Core.MediaFiles.EpisodeImport.ImportMode;
using MovieImportDecision = NzbDrone.Core.MediaFiles.MovieImport.ImportDecision;
using MovieImportDecisionMaker = NzbDrone.Core.MediaFiles.MovieImport.IMakeImportDecision;
using MovieImportMode = NzbDrone.Core.MediaFiles.MovieImport.ImportMode;

namespace NzbDrone.Core.Reorganise
{
    public interface ILooseMediaService
    {
        // The video files in a root folder (or one folder of it) that no series or movie of the library holds yet: each
        // with where it would go, or why it can't go anywhere.
        List<ReorganisePreviewItem> Find(string folder);

        // Moves the files that can go into their series or movie. Returns how many were moved.
        int Import(List<ReorganisePreviewItem> items);
    }

    /// <summary>
    /// Roadmap §4.12 "Identify": a root folder can hold files that never made it into the library, such as episodes left
    /// loose in the TV root or a release folder next to the series folders. Each one is matched to a series or movie of
    /// the library by its name, and moved into it the way a Manual Import does (named by the naming format on the way).
    /// </summary>
    public class LooseMediaService : ILooseMediaService
    {
        private readonly IRootFolderService _rootFolderService;
        private readonly ISeriesService _seriesService;
        private readonly IMovieService _movieService;
        private readonly IManualImportService _manualImportService;
        private readonly IExecute<ManualImportCommand> _manualImport;
        private readonly IMovieParsingService _movieParsingService;
        private readonly MovieImportDecisionMaker _movieImportDecisionMaker;
        private readonly MediaFiles.MovieImport.IImportApprovedMovie _importApprovedMovie;
        private readonly IDiskScanService _diskScanService;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IBuildMovieFileNames _movieFileNameBuilder;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public LooseMediaService(IRootFolderService rootFolderService,
                                 ISeriesService seriesService,
                                 IMovieService movieService,
                                 IManualImportService manualImportService,
                                 IExecute<ManualImportCommand> manualImport,
                                 IMovieParsingService movieParsingService,
                                 MovieImportDecisionMaker movieImportDecisionMaker,
                                 MediaFiles.MovieImport.IImportApprovedMovie importApprovedMovie,
                                 IDiskScanService diskScanService,
                                 IBuildFileNames fileNameBuilder,
                                 IBuildMovieFileNames movieFileNameBuilder,
                                 IDiskProvider diskProvider,
                                 Logger logger)
        {
            _rootFolderService = rootFolderService;
            _seriesService = seriesService;
            _movieService = movieService;
            _manualImportService = manualImportService;
            _manualImport = manualImport;
            _movieParsingService = movieParsingService;
            _movieImportDecisionMaker = movieImportDecisionMaker;
            _importApprovedMovie = importApprovedMovie;
            _diskScanService = diskScanService;
            _fileNameBuilder = fileNameBuilder;
            _movieFileNameBuilder = movieFileNameBuilder;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public List<ReorganisePreviewItem> Find(string folder)
        {
            var items = new List<ReorganisePreviewItem>();
            var libraryPaths = _seriesService.GetAllSeriesPaths().Values
                .Concat(_movieService.AllMoviePaths().Values)
                .ToList();

            foreach (var rootFolder in _rootFolderService.All().Where(r => r.Library is LibraryType.Series or LibraryType.Movies))
            {
                var root = rootFolder.Path.TrimEnd('/', '\\');

                if (folder.IsNotNullOrWhiteSpace() && !folder.PathEquals(root) && !folder.IsParentPath(root) && !root.IsParentPath(folder))
                {
                    continue;
                }

                if (!_diskProvider.FolderExists(root))
                {
                    items.Add(new ReorganisePreviewItem
                    {
                        Kind = ReorganiseKinds.Import,
                        MediaType = rootFolder.Library == LibraryType.Movies ? ReorganiseMediaTypes.Movie : ReorganiseMediaTypes.Episode,
                        From = root,
                        Problem = $"The root folder {root} can't be reached. Is its drive connected?"
                    });

                    continue;
                }

                foreach (var entry in Entries(root, folder, libraryPaths))
                {
                    try
                    {
                        items.AddRange(rootFolder.Library == LibraryType.Movies ? FindMovieFiles(entry) : FindEpisodeFiles(entry));
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Unable to look at '{0}'", entry);

                        items.Add(new ReorganisePreviewItem
                        {
                            Kind = ReorganiseKinds.Import,
                            MediaType = rootFolder.Library == LibraryType.Movies ? ReorganiseMediaTypes.Movie : ReorganiseMediaTypes.Episode,
                            From = entry,
                            Problem = $"It couldn't be read: {ex.Message}"
                        });
                    }
                }
            }

            return items;
        }

        public int Import(List<ReorganisePreviewItem> items)
        {
            var moved = 0;

            foreach (var series in items.Where(i => i.EpisodeImport != null).GroupBy(i => i.MediaId))
            {
                try
                {
                    _manualImport.Execute(new ManualImportCommand
                    {
                        ImportMode = EpisodeImportMode.Move,
                        Files = series.Select(i => i.EpisodeImport).Select(i => new ManualImportFile
                        {
                            Path = i.Path,
                            FolderName = i.FolderName,
                            SeriesId = i.Series.Id,
                            EpisodeIds = i.Episodes.Select(e => e.Id).ToList(),
                            Quality = i.Quality,
                            Languages = i.Languages,
                            ReleaseGroup = i.ReleaseGroup,
                            IndexerFlags = i.IndexerFlags,
                            ReleaseType = i.ReleaseType
                        }).ToList()
                    });
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to move the loose files of '{0}' into it", series.First().Title);
                }

                moved += Moved(series);
            }

            foreach (var movie in items.Where(i => i.MovieImport != null).GroupBy(i => i.MediaId))
            {
                try
                {
                    _importApprovedMovie.Import(movie.Select(i => i.MovieImport).ToList(), false, null, MovieImportMode.Move);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to move the loose files of '{0}' into it", movie.First().Title);
                }

                moved += Moved(movie);
            }

            return moved;
        }

        // A file that was moved isn't where it was any more; one that failed still is, and the log says why.
        private int Moved(IEnumerable<ReorganisePreviewItem> items)
        {
            var moved = 0;

            foreach (var item in items)
            {
                if (_diskProvider.FileExists(item.From))
                {
                    _logger.Warn("'{0}' wasn't moved into {1}", item.From, item.Title);
                }
                else
                {
                    moved++;
                }
            }

            return moved;
        }

        // What lies directly in the root folder (or the folder asked for) and isn't a library folder or inside one.
        private IEnumerable<string> Entries(string root, string folder, List<string> libraryPaths)
        {
            bool IsLibrary(string path) => libraryPaths.Any(p => p.PathEquals(path) || p.IsParentPath(path));

            if (folder.IsNotNullOrWhiteSpace() && root.IsParentPath(folder))
            {
                if (IsLibrary(folder) || (!_diskProvider.FolderExists(folder) && !_diskProvider.FileExists(folder)))
                {
                    return Array.Empty<string>();
                }

                return new[] { folder };
            }

            var folders = _diskScanService.FilterPaths(root, _diskProvider.GetDirectories(root), false)
                .Where(f => !Path.GetFileName(f).StartsWith(".") && !IsLibrary(f) && !libraryPaths.Any(p => f.IsParentPath(p)));

            var files = _diskScanService.FilterPaths(root, _diskScanService.GetVideoFiles(root, false));

            return files.Concat(folders).OrderBy(p => p, StringComparer.InvariantCultureIgnoreCase);
        }

        private IEnumerable<ReorganisePreviewItem> FindEpisodeFiles(string entry)
        {
            var found = _manualImportService.GetMediaFiles(entry, null, null, true);

            if (!found.Any() && _diskProvider.FolderExists(entry))
            {
                yield break;
            }

            foreach (var item in found)
            {
                var preview = new ReorganisePreviewItem
                {
                    Kind = ReorganiseKinds.Import,
                    MediaType = ReorganiseMediaTypes.Episode,
                    MediaId = item.Series?.Id ?? 0,
                    Title = item.Series?.Title,
                    From = item.Path,
                    To = item.Series?.Path
                };

                if (item.Series == null)
                {
                    preview.Problem = "Its name doesn't match a series in the library. Add the series first, or use Manual Import to choose one";
                }
                else if (item.Episodes == null || !item.Episodes.Any())
                {
                    preview.Problem = item.Rejections.Any()
                        ? Describe(item.Rejections.Select(r => r.Message))
                        : $"Its name doesn't say which episode of {item.Series.Title} it is";
                }
                else if (item.Rejections.Any())
                {
                    preview.Problem = Describe(item.Rejections.Select(r => r.Message));
                }
                else
                {
                    preview.To = EpisodeDestination(item);
                    preview.EpisodeImport = item;
                }

                yield return preview;
            }
        }

        private IEnumerable<ReorganisePreviewItem> FindMovieFiles(string entry)
        {
            var isFolder = _diskProvider.FolderExists(entry);
            var files = isFolder
                ? _diskScanService.FilterPaths(entry, _diskScanService.GetVideoFiles(entry))
                : new List<string> { entry };

            if (!files.Any())
            {
                yield break;
            }

            var name = isFolder ? Path.GetFileName(entry) : Path.GetFileNameWithoutExtension(entry);
            var movie = _movieParsingService.GetMovie(name);

            if (movie == null)
            {
                foreach (var file in files)
                {
                    yield return new ReorganisePreviewItem
                    {
                        Kind = ReorganiseKinds.Import,
                        MediaType = ReorganiseMediaTypes.Movie,
                        From = file,
                        Problem = "Its name doesn't match a movie in the library. Add the movie first (Library Import → Movies)"
                    };
                }

                yield break;
            }

            var folderInfo = isFolder ? MovieParser.ParseMovieTitle(name, true) : null;
            var decisions = _movieImportDecisionMaker.GetImportDecisions(files, movie, null, folderInfo, true, true);

            foreach (var decision in decisions)
            {
                var preview = new ReorganisePreviewItem
                {
                    Kind = ReorganiseKinds.Import,
                    MediaType = ReorganiseMediaTypes.Movie,
                    MediaId = movie.Id,
                    Title = movie.Title,
                    From = decision.LocalMovie.Path,
                    To = movie.Path
                };

                if (decision.Approved)
                {
                    preview.To = MovieDestination(movie, decision);
                    preview.MovieImport = decision;
                }
                else
                {
                    preview.Problem = Describe(decision.Rejections.Select(r => r.Message));
                }

                yield return preview;
            }
        }

        // Where the import will put the file. Only shown, so a naming token that needs more than the file name has is
        // left to the import itself.
        private string EpisodeDestination(ManualImportItem item)
        {
            try
            {
                var file = new EpisodeFile
                {
                    Quality = item.Quality,
                    Languages = item.Languages,
                    ReleaseGroup = item.ReleaseGroup,
                    RelativePath = item.RelativePath,
                    SceneName = Path.GetFileNameWithoutExtension(item.Path)
                };

                return _fileNameBuilder.BuildFilePath(item.Episodes, item.Series, file, Path.GetExtension(item.Path));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to work out where '{0}' will go", item.Path);

                return item.Series.Path;
            }
        }

        private string MovieDestination(Movie movie, MovieImportDecision decision)
        {
            try
            {
                var file = new MovieFile
                {
                    Quality = decision.LocalMovie.Quality,
                    Languages = decision.LocalMovie.Languages,
                    ReleaseGroup = decision.LocalMovie.ReleaseGroup,
                    SceneName = Path.GetFileNameWithoutExtension(decision.LocalMovie.Path),
                    MediaInfo = decision.LocalMovie.MediaInfo
                };

                return _movieFileNameBuilder.BuildFilePath(movie, _movieFileNameBuilder.BuildFileName(movie, file), Path.GetExtension(decision.LocalMovie.Path));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to work out where '{0}' will go", decision.LocalMovie.Path);

                return movie.Path;
            }
        }

        private static string Describe(IEnumerable<string> reasons)
        {
            return string.Join("; ", reasons.Where(r => r.IsNotNullOrWhiteSpace()).Distinct());
        }
    }
}
