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
    /// as good for, stay where they are, and the command's results say why.
    /// </summary>
    public class MergeIntoSeriesCommand : Command
    {
        public int SeriesId { get; set; }
        public List<string> Paths { get; set; } = new();

        // What happened to each file (or path that couldn't be merged at all), filled in as the merge runs.
        public List<MergeIntoSeriesResult> Results { get; set; } = new();

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override bool IsLongRunning => true;
        public override string CompletionMessage => "Merge finished";
    }

    public class MergeIntoSeriesResult
    {
        public string Path { get; set; }
        public bool Merged { get; set; }
        public string Reason { get; set; }
    }

    public class MergeIntoSeriesService : IExecute<MergeIntoSeriesCommand>
    {
        private readonly IManualImportService _manualImportService;
        private readonly ISeriesService _seriesService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public MergeIntoSeriesService(IManualImportService manualImportService,
                                      ISeriesService seriesService,
                                      IRootFolderService rootFolderService,
                                      IDiskProvider diskProvider,
                                      Logger logger)
        {
            _manualImportService = manualImportService;
            _seriesService = seriesService;
            _rootFolderService = rootFolderService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void Execute(MergeIntoSeriesCommand message)
        {
            var series = _seriesService.GetSeries(message.SeriesId);
            var results = message.Results;

            foreach (var path in message.Paths.Distinct())
            {
                var problem = WhyNotMergeable(path, series);

                if (problem != null)
                {
                    Leave(results, path, problem);
                    continue;
                }

                _logger.ProgressInfo("Matching the files in '{0}' to {1}", path, series.Title);

                var items = _manualImportService.GetMediaFiles(path, null, series.Id, true);
                var importable = new List<ManualImportItem>();

                foreach (var item in items)
                {
                    var reason = DescribeProblem(item);

                    if (reason == null)
                    {
                        importable.Add(item);
                    }
                    else
                    {
                        Leave(results, item.Path, reason);
                    }
                }

                if (importable.Any())
                {
                    _logger.ProgressInfo("Merging {0} file(s) from '{1}' into {2}", importable.Count, path, series.Title);

                    var imported = _manualImportService.Import(new ManualImportCommand
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
                    }) ?? new List<ImportResult>();

                    foreach (var item in importable)
                    {
                        var result = imported.FirstOrDefault(r => r.ImportDecision.LocalEpisode.Path.PathEquals(item.Path));

                        // A file that failed to import is still where it was.
                        if (_diskProvider.FileExists(item.Path))
                        {
                            Leave(results, item.Path, result is { Errors.Count: > 0 } ? string.Join("; ", result.Errors) : "It couldn't be moved");
                        }
                        else
                        {
                            results.Add(new MergeIntoSeriesResult { Path = item.Path, Merged = true });
                        }
                    }
                }

                RemoveFolderIfEmpty(path);
            }

            _logger.ProgressInfo("Merged {0} file(s) into {1}; {2} left in place", results.Count(r => r.Merged), series.Title, results.Count(r => !r.Merged));
        }

        private void Leave(List<MergeIntoSeriesResult> results, string path, string reason)
        {
            _logger.Info("'{0}' isn't merged: {1}", path, reason);
            results.Add(new MergeIntoSeriesResult { Path = path, Merged = false, Reason = reason });
        }

        // Only something inside a root folder, and not the root folder or a library folder itself, is moved.
        private string WhyNotMergeable(string path, Series series)
        {
            if (path.IsNullOrWhiteSpace() || (!_diskProvider.FolderExists(path) && !_diskProvider.FileExists(path)))
            {
                return "It isn't there any more";
            }

            var rootFolder = _rootFolderService.GetBestRootFolderPath(path);

            if (rootFolder.IsNullOrWhiteSpace() || path.PathEquals(rootFolder) || !rootFolder.IsParentPath(path))
            {
                return "It isn't inside a root folder";
            }

            if (path.PathEquals(series.Path) || series.Path.IsParentPath(path))
            {
                return $"It's already in the folder of {series.Title}";
            }

            return null;
        }

        // Why a file can't be merged, or null when it can. The folder-name check is skipped: the series was chosen
        // by hand, so a batch folder named for another season or series doesn't stop its episodes from matching.
        private static string DescribeProblem(ManualImportItem item)
        {
            if (item.Episodes is not { Count: > 0 })
            {
                return "It doesn't match an episode of the series";
            }

            var rejections = item.Rejections.Where(r => r.Reason != ImportRejectionReason.EpisodeUnexpected).ToList();

            return rejections.Any() ? string.Join("; ", rejections.Select(r => r.Message)) : null;
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
