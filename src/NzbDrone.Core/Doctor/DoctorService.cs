using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Doctor
{
    public interface IDoctorService
    {
        List<DoctorFlag> GetFlags();
        int Scan();
        void Recover(int flagId);
        void Delete(int flagId);
        int DeleteExpired();
    }

    public class DoctorService : IDoctorService, IExecute<DoctorCommand>
    {
        private readonly IDoctorFlagRepository _flags;
        private readonly ISeriesService _seriesService;
        private readonly IMovieService _movieService;
        private readonly IMediaFileService _episodeFileService;
        private readonly IMovieFileService _movieFileService;
        private readonly IDeleteMediaFiles _deleteEpisodeFiles;
        private readonly IDeleteMovieFiles _deleteMovieFiles;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public DoctorService(IDoctorFlagRepository flags,
                             ISeriesService seriesService,
                             IMovieService movieService,
                             IMediaFileService episodeFileService,
                             IMovieFileService movieFileService,
                             IDeleteMediaFiles deleteEpisodeFiles,
                             IDeleteMovieFiles deleteMovieFiles,
                             IConfigService configService,
                             Logger logger)
        {
            _flags = flags;
            _seriesService = seriesService;
            _movieService = movieService;
            _episodeFileService = episodeFileService;
            _movieFileService = movieFileService;
            _deleteEpisodeFiles = deleteEpisodeFiles;
            _deleteMovieFiles = deleteMovieFiles;
            _configService = configService;
            _logger = logger;
        }

        public List<DoctorFlag> GetFlags()
        {
            return _flags.All().Where(f => f.State == DoctorFlagState.Flagged).OrderBy(f => f.FlaggedAt).ToList();
        }

        public int Scan()
        {
            var candidates = new List<DoctorCandidate>();
            var existing = _flags.All().ToList();
            var protectedFiles = existing.Where(f => f.State == DoctorFlagState.Recovered)
                                         .Select(f => (f.MediaType, f.FileId))
                                         .ToHashSet();

            foreach (var series in _seriesService.GetAllSeries())
            {
                foreach (var file in _episodeFileService.GetFilesBySeries(series.Id))
                {
                    var episodeIds = file.Episodes.Value.Select(e => e.Id).OrderBy(i => i).ToList();

                    if (episodeIds.Empty())
                    {
                        continue;
                    }

                    candidates.Add(new DoctorCandidate
                    {
                        MediaType = DoctorMediaType.Series,
                        ItemId = series.Id,
                        GroupKey = string.Join(",", episodeIds),
                        FileId = file.Id,
                        Size = file.Size,
                        DateAdded = file.DateAdded,
                        Quality = file.Quality,
                        MediaInfo = file.MediaInfo,
                        IsProtected = protectedFiles.Contains((DoctorMediaType.Series, file.Id))
                    });
                }
            }

            foreach (var movie in _movieService.GetAllMovies())
            {
                foreach (var file in _movieFileService.GetFilesByMovie(movie.Id))
                {
                    candidates.Add(new DoctorCandidate
                    {
                        MediaType = DoctorMediaType.Movie,
                        ItemId = movie.Id,
                        GroupKey = "movie",
                        FileId = file.Id,
                        Size = file.Size,
                        DateAdded = file.DateAdded,
                        Quality = file.Quality,
                        MediaInfo = file.MediaInfo,
                        IsProtected = protectedFiles.Contains((DoctorMediaType.Movie, file.Id))
                    });
                }
            }

            var duplicates = DoctorAnalyzer.FindDuplicates(candidates);
            var liveFiles = candidates.Select(c => (c.MediaType, c.FileId)).ToHashSet();
            var current = duplicates.Select(d => (d.Worse.MediaType, d.Worse.FileId)).ToHashSet();
            var now = DateTime.UtcNow;

            // Forget flags that no longer apply: the copy is gone, or it is no longer a worse duplicate.
            foreach (var flag in existing)
            {
                var key = (flag.MediaType, flag.FileId);

                if (!liveFiles.Contains(key) || (flag.State == DoctorFlagState.Flagged && !current.Contains(key)))
                {
                    _flags.Delete(flag);
                }
            }

            var flagged = existing.Where(f => f.State == DoctorFlagState.Flagged).ToDictionary(f => (f.MediaType, f.FileId));
            var added = 0;

            foreach (var duplicate in duplicates)
            {
                var key = (duplicate.Worse.MediaType, duplicate.Worse.FileId);

                if (flagged.TryGetValue(key, out var flag))
                {
                    if (liveFiles.Contains(key) && (flag.KeptFileId != duplicate.Better.FileId || flag.Reason != duplicate.Reason))
                    {
                        flag.KeptFileId = duplicate.Better.FileId;
                        flag.Reason = duplicate.Reason;
                        _flags.Update(flag);
                    }

                    continue;
                }

                _flags.Insert(new DoctorFlag
                {
                    MediaType = duplicate.Worse.MediaType,
                    ItemId = duplicate.Worse.ItemId,
                    FileId = duplicate.Worse.FileId,
                    KeptFileId = duplicate.Better.FileId,
                    Reason = duplicate.Reason,
                    State = DoctorFlagState.Flagged,
                    FlaggedAt = now
                });
                added++;
            }

            _logger.Info("Doctor flagged {0} new duplicate file(s)", added);

            return added;
        }

        // Keeps the file. The other copies of the same thing are flagged on the next scan, so this also works for
        // choosing the flagged copy over the one Doctor kept.
        public void Recover(int flagId)
        {
            var flag = _flags.Get(flagId);

            flag.State = DoctorFlagState.Recovered;
            _flags.Update(flag);

            Scan();
        }

        public void Delete(int flagId)
        {
            var flag = _flags.Get(flagId);

            if (flag.MediaType == DoctorMediaType.Series)
            {
                var file = _episodeFileService.Get(flag.FileId);
                _deleteEpisodeFiles.DeleteEpisodeFile(_seriesService.GetSeries(file.SeriesId), file);
            }
            else
            {
                var file = _movieFileService.GetMovie(flag.FileId);
                _deleteMovieFiles.DeleteMovieFile(_movieService.GetMovie(file.MovieId), file);
            }

            _flags.Delete(flag);
        }

        public int DeleteExpired()
        {
            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _configService.DoctorRetentionDays));
            var deleted = 0;

            foreach (var flag in GetFlags().Where(f => f.FlaggedAt <= cutoff))
            {
                try
                {
                    Delete(flag.Id);
                    deleted++;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Doctor could not delete flagged file {0}", flag.FileId);
                }
            }

            return deleted;
        }

        public void Execute(DoctorCommand message)
        {
            if (message.Trigger == CommandTrigger.Scheduled && !_configService.DoctorEnabled)
            {
                _logger.Debug("Doctor is switched off");
                return;
            }

            Scan();

            // Only the scheduled run deletes; running Doctor by hand just looks.
            if (message.Trigger == CommandTrigger.Scheduled)
            {
                DeleteExpired();
            }
        }
    }
}
