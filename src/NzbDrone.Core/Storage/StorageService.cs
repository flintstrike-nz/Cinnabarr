using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Commands;
using NzbDrone.Core.Queue;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Commands;

namespace NzbDrone.Core.Storage
{
    public class StorageLibraryPlan
    {
        public LibraryType Library { get; set; }
        public List<StorageDrive> Drives { get; set; } = new List<StorageDrive>();

        // What is on its way to each drive, by drive path.
        public Dictionary<string, long> Incoming { get; set; } = new Dictionary<string, long>();
        public List<StorageMove> Moves { get; set; } = new List<StorageMove>();
    }

    public interface IStorageService
    {
        List<StorageLibraryPlan> GetPlan();
        int Balance();
    }

    public class StorageService : IStorageService, IExecute<StorageBalanceCommand>
    {
        private const long DefaultEpisodeBytes = 1_500_000_000;
        private const long DefaultMovieBytes = 6_000_000_000;

        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskProvider _diskProvider;
        private readonly ISeriesService _seriesService;
        private readonly IMovieService _movieService;
        private readonly IEpisodeService _episodeService;
        private readonly IMediaFileService _episodeFileService;
        private readonly IMovieFileService _movieFileService;
        private readonly IQueueService _queueService;
        private readonly IManageCommandQueue _commandQueue;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public StorageService(IRootFolderService rootFolderService,
                              IDiskProvider diskProvider,
                              ISeriesService seriesService,
                              IMovieService movieService,
                              IEpisodeService episodeService,
                              IMediaFileService episodeFileService,
                              IMovieFileService movieFileService,
                              IQueueService queueService,
                              IManageCommandQueue commandQueue,
                              IConfigService configService,
                              Logger logger)
        {
            _rootFolderService = rootFolderService;
            _diskProvider = diskProvider;
            _seriesService = seriesService;
            _movieService = movieService;
            _episodeService = episodeService;
            _episodeFileService = episodeFileService;
            _movieFileService = movieFileService;
            _queueService = queueService;
            _commandQueue = commandQueue;
            _configService = configService;
            _logger = logger;
        }

        public List<StorageLibraryPlan> GetPlan()
        {
            var settings = new StoragePlanSettings
            {
                TolerancePercent = _configService.StorageTolerancePercent,
                ReservePercent = _configService.StorageReservePercent,
                MaxBytesPerRun = _configService.StorageMaxMoveGb * 1024L * 1024 * 1024
            };

            var roots = _rootFolderService.All();
            var queue = _queueService.GetQueue();
            var plans = new List<StorageLibraryPlan>();

            foreach (var library in new[] { LibraryType.Series, LibraryType.Movies })
            {
                var drives = roots
                    .Where(r => r.Library == library && _diskProvider.FolderExists(r.Path))
                    .Select(r => new StorageDrive
                    {
                        Path = r.Path,
                        TotalSpace = _diskProvider.GetTotalSize(r.Path) ?? 0,
                        FreeSpace = _diskProvider.GetAvailableSpace(r.Path) ?? 0
                    })
                    .ToList();

                var items = library == LibraryType.Series ? GetSeriesItems(drives, queue) : GetMovieItems(drives, queue);
                var plan = new StorageLibraryPlan { Library = library, Drives = drives };

                foreach (var drive in drives)
                {
                    plan.Incoming[drive.Path] = items.Where(i => string.Equals(i.DrivePath, drive.Path, StringComparison.OrdinalIgnoreCase)).Sum(i => i.IncomingBytes);
                }

                plan.Moves = StoragePlanner.Plan(drives, items, settings);
                plans.Add(plan);
            }

            return plans;
        }

        public int Balance()
        {
            var moves = GetPlan().SelectMany(p => p.Moves).ToList();

            foreach (var move in moves)
            {
                var destination = Path.Combine(move.ToDrive, Path.GetFileName(move.Item.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

                _logger.Info("Storage: moving {0} from {1} to {2}", move.Item.Title, move.FromDrive, move.ToDrive);

                if (move.Item.MediaType == StorageMediaType.Series)
                {
                    _commandQueue.Push(new MoveSeriesCommand { SeriesId = move.Item.Id, SourcePath = move.Item.Path, DestinationPath = destination }, trigger: CommandTrigger.Scheduled);
                }
                else
                {
                    _commandQueue.Push(new MoveMovieCommand { MovieId = move.Item.Id, SourcePath = move.Item.Path, DestinationPath = destination }, trigger: CommandTrigger.Scheduled);
                }
            }

            return moves.Count;
        }

        public void Execute(StorageBalanceCommand message)
        {
            // A scheduled run only moves things when it has been switched on; pressing the button always does.
            if (message.Trigger == CommandTrigger.Scheduled && !_configService.StorageBalancerEnabled)
            {
                _logger.Debug("Storage balancing is switched off");
                return;
            }

            _logger.Info("Storage: {0} move(s) started", Balance());
        }

        private List<StorageItem> GetSeriesItems(List<StorageDrive> drives, List<NzbDrone.Core.Queue.Queue> queue)
        {
            var items = new List<StorageItem>();
            var horizon = DateTime.UtcNow.AddDays(_configService.StorageForecastDays);
            var upcoming = _episodeService.EpisodesBetweenDates(DateTime.UtcNow.AddDays(-30), horizon, false, false)
                                          .Where(e => !e.HasFile)
                                          .GroupBy(e => e.SeriesId)
                                          .ToDictionary(g => g.Key, g => g.Count());

            foreach (var series in _seriesService.GetAllSeries())
            {
                var drive = DriveOf(series.Path, drives);

                if (drive == null)
                {
                    continue;
                }

                var files = _episodeFileService.GetFilesBySeries(series.Id);
                var size = files.Sum(f => f.Size);
                var average = files.Count > 0 ? size / files.Count : DefaultEpisodeBytes;
                var forecast = upcoming.TryGetValue(series.Id, out var count) ? count * average : 0;
                var inFlight = queue.Where(q => q.Series?.Id == series.Id).ToList();

                items.Add(new StorageItem
                {
                    MediaType = StorageMediaType.Series,
                    Id = series.Id,
                    Title = series.Title,
                    Path = series.Path,
                    DrivePath = drive,
                    Size = size,
                    IncomingBytes = forecast + (long)inFlight.Sum(q => q.SizeLeft),
                    IsBusy = inFlight.Count > 0
                });
            }

            return items;
        }

        private List<StorageItem> GetMovieItems(List<StorageDrive> drives, List<NzbDrone.Core.Queue.Queue> queue)
        {
            var items = new List<StorageItem>();
            var movies = _movieService.GetAllMovies();
            var sizes = movies.ToDictionary(m => m.Id, m => _movieFileService.GetFilesByMovie(m.Id).Sum(f => f.Size));
            var withFiles = sizes.Values.Where(s => s > 0).ToList();
            var average = withFiles.Count > 0 ? withFiles.Sum() / withFiles.Count : DefaultMovieBytes;

            foreach (var movie in movies)
            {
                var drive = DriveOf(movie.Path, drives);

                if (drive == null)
                {
                    continue;
                }

                var inFlight = queue.Where(q => q.Movie?.Id == movie.Id).ToList();
                var forecast = movie.Monitored && !movie.HasFile && movie.IsAvailable() ? average : 0;

                items.Add(new StorageItem
                {
                    MediaType = StorageMediaType.Movie,
                    Id = movie.Id,
                    Title = movie.Title,
                    Path = movie.Path,
                    DrivePath = drive,
                    Size = sizes[movie.Id],
                    IncomingBytes = forecast + (long)inFlight.Sum(q => q.SizeLeft),
                    IsBusy = inFlight.Count > 0
                });
            }

            return items;
        }

        private static string DriveOf(string path, List<StorageDrive> drives)
        {
            return string.IsNullOrWhiteSpace(path)
                ? null
                : drives.Where(d => path.StartsWith(d.Path, StringComparison.OrdinalIgnoreCase)).OrderByDescending(d => d.Path.Length).FirstOrDefault()?.Path;
        }
    }
}
