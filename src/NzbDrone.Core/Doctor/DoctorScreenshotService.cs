using System;
using System.Diagnostics;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Doctor
{
    public interface IDoctorScreenshotService
    {
        // The path of a jpeg frame from the file (index 0 to 2: early, middle and late), or null when one cannot be made.
        string GetScreenshot(DoctorMediaType mediaType, int fileId, int index);
    }

    public class DoctorScreenshotService : IDoctorScreenshotService
    {
        private static readonly double[] Positions = { 0.2, 0.5, 0.8 };

        private readonly IMediaFileService _episodeFileService;
        private readonly IMovieFileService _movieFileService;
        private readonly ISeriesService _seriesService;
        private readonly IMovieService _movieService;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public DoctorScreenshotService(IMediaFileService episodeFileService,
                                       IMovieFileService movieFileService,
                                       ISeriesService seriesService,
                                       IMovieService movieService,
                                       IAppFolderInfo appFolderInfo,
                                       IDiskProvider diskProvider,
                                       Logger logger)
        {
            _episodeFileService = episodeFileService;
            _movieFileService = movieFileService;
            _seriesService = seriesService;
            _movieService = movieService;
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public string GetScreenshot(DoctorMediaType mediaType, int fileId, int index)
        {
            index = Math.Clamp(index, 0, Positions.Length - 1);

            string path;
            long size;
            MediaInfoModel info;

            if (mediaType == DoctorMediaType.Series)
            {
                var file = _episodeFileService.Get(fileId);
                path = Path.Combine(_seriesService.GetSeries(file.SeriesId).Path, file.RelativePath);
                size = file.Size;
                info = file.MediaInfo;
            }
            else
            {
                var file = _movieFileService.GetMovie(fileId);
                path = Path.Combine(_movieService.GetMovie(file.MovieId).Path, file.RelativePath);
                size = file.Size;
                info = file.MediaInfo;
            }

            var folder = Path.Combine(_appFolderInfo.AppDataFolder, "doctor-screenshots");
            var target = Path.Combine(folder, $"{mediaType}-{fileId}-{size}-{index}.jpg");

            if (_diskProvider.FileExists(target))
            {
                return target;
            }

            if (!_diskProvider.FileExists(path))
            {
                return null;
            }

            var runtime = info?.RunTime.TotalSeconds ?? 0;
            var seconds = runtime > 0 ? runtime * Positions[index] : 60 * (index + 1);

            _diskProvider.CreateFolder(folder);

            return RunFfmpeg(path, target, seconds) ? target : null;
        }

        private bool RunFfmpeg(string source, string target, double seconds)
        {
            var start = new ProcessStartInfo("ffmpeg")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in new[] { "-v", "error", "-ss", seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "-i", source, "-frames:v", "1", "-vf", "scale=480:-2", "-q:v", "4", "-y", target })
            {
                start.ArgumentList.Add(argument);
            }

            try
            {
                using var process = Process.Start(start);

                process.StandardOutput.ReadToEndAsync();
                process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(30000))
                {
                    process.Kill();
                    return false;
                }

                return process.ExitCode == 0 && _diskProvider.FileExists(target);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not make a screenshot of {0}", source);
                return false;
            }
        }
    }
}
