using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles.EpisodeImport.Manual
{
    /// <summary>
    /// Merges folders (or files) that belong to a series already in the library into that series: each video file is
    /// matched to the series' episodes and moved into the series' folder, like a Manual Import would, and the folder
    /// is removed once nothing is left in it. Files that can't be matched, or that the library already has something
    /// as good for, stay where they are.
    /// </summary>
    public class MergeIntoSeriesCommand : Command
    {
        public int SeriesId { get; set; }
        public List<string> Paths { get; set; } = new();

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override bool IsLongRunning => true;
        public override string CompletionMessage => "Merge finished";
    }

    public class MergeIntoSeriesService : IExecute<MergeIntoSeriesCommand>
    {
        private readonly IManualImportService _manualImportService;
        private readonly IExecute<ManualImportCommand> _manualImport;
        private readonly ISeriesService _seriesService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public MergeIntoSeriesService(IManualImportService manualImportService,
                                      IExecute<ManualImportCommand> manualImport,
                                      ISeriesService seriesService,
                                      IRootFolderService rootFolderService,
                                      IDiskProvider diskProvider,
                                      Logger logger)
        {
            _manualImportService = manualImportService;
            _manualImport = manualImport;
            _seriesService = seriesService;
            _rootFolderService = rootFolderService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void Execute(MergeIntoSeriesCommand message)
        {
            var series = _seriesService.GetSeries(message.SeriesId);
            var merged = 0;
            var left = 0;

            foreach (var path in message.Paths.Distinct())
            {
                if (!IsMergeable(path, series))
                {
                    left++;
                    continue;
                }

                var items = _manualImportService.GetMediaFiles(path, null, series.Id, true);

                var importable = items.Where(i => i.Episodes is { Count: > 0 } && !i.Rejections.Any()).ToList();

                foreach (var item in items.Except(importable))
                {
                    _logger.Info("'{0}' isn't merged into {1}: {2}", item.Path, series.Title, DescribeProblem(item));
                    left++;
                }

                if (importable.Any())
                {
                    _logger.ProgressInfo("Merging {0} file(s) from '{1}' into {2}", importable.Count, path, series.Title);

                    _manualImport.Execute(new ManualImportCommand
                    {
                        ImportMode = ImportMode.Move,
                        Files = importable.Select(i => new ManualImportFile
                        {
                            Path = i.Path,
                            FolderName = i.FolderName,
                            SeriesId = series.Id,
                            EpisodeIds = i.Episodes.Select(e => e.Id).ToList(),
                            Quality = i.Quality,
                            Languages = i.Languages,
                            ReleaseGroup = i.ReleaseGroup,
                            IndexerFlags = i.IndexerFlags,
                            ReleaseType = i.ReleaseType
                        }).ToList()
                    });

                    // A file that failed to import is still where it was.
                    merged += importable.Count(i => !_diskProvider.FileExists(i.Path));
                    left += importable.Count(i => _diskProvider.FileExists(i.Path));
                }

                RemoveFolderIfEmpty(path);
            }

            _logger.ProgressInfo("Merged {0} file(s) into {1}; {2} left in place", merged, series.Title, left);
        }

        // Only something inside a root folder, and not the root folder or a library folder itself, is moved.
        private bool IsMergeable(string path, Series series)
        {
            if (path.IsNullOrWhiteSpace() || (!_diskProvider.FolderExists(path) && !_diskProvider.FileExists(path)))
            {
                _logger.Warn("'{0}' doesn't exist; it isn't merged", path);

                return false;
            }

            var rootFolder = _rootFolderService.GetBestRootFolderPath(path);

            if (rootFolder.IsNullOrWhiteSpace() || path.PathEquals(rootFolder) || !rootFolder.IsParentPath(path))
            {
                _logger.Warn("'{0}' isn't inside a root folder; it isn't merged", path);

                return false;
            }

            if (path.PathEquals(series.Path) || series.Path.IsParentPath(path))
            {
                _logger.Warn("'{0}' is already in the folder of {1}; it isn't merged", path, series.Title);

                return false;
            }

            return true;
        }

        private static string DescribeProblem(ManualImportItem item)
        {
            if (item.Rejections.Any())
            {
                return string.Join(", ", item.Rejections.Select(r => r.Message));
            }

            return "it doesn't match an episode of the series";
        }

        // Nothing is deleted but folders: the folder goes only when no file is left in it.
        private void RemoveFolderIfEmpty(string path)
        {
            if (!_diskProvider.FolderExists(path))
            {
                return;
            }

            _diskProvider.RemoveEmptySubfolders(path);

            if (!_diskProvider.GetFiles(path, true).Any() && _diskProvider.FolderExists(path))
            {
                _diskProvider.DeleteFolder(path, true);
            }
            else
            {
                _logger.Info("'{0}' still holds files, so it stays", path);
            }
        }
    }
}
