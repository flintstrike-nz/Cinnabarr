using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Queue;
using NzbDrone.Core.SeriesStats;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Requests
{
    public enum LibraryItemStatus
    {
        // Not in Cinnabarr.
        NotInLibrary,

        // In Cinnabarr, monitored, no files yet.
        Wanted,

        // In Cinnabarr, not monitored, no files.
        Unmonitored,

        // Something for it is in the download queue.
        Downloading,

        // A series with some, but not all, of its monitored episodes.
        Partial,

        // Everything monitored is on disk.
        Available
    }

    public class LibraryItemState
    {
        public RequestMediaType MediaType { get; set; }
        public int ExternalId { get; set; }

        // The series or movie id in Cinnabarr, or null when it is not in the library.
        public int? Id { get; set; }
        public LibraryItemStatus Status { get; set; }
    }

    public interface ILibraryStatusService
    {
        List<LibraryItemState> GetStatus(IEnumerable<int> tvdbIds, IEnumerable<int> tmdbIds);
    }

    /// <summary>
    /// Whether items are already in Cinnabarr, so a client can show "Requested", "Downloading" or "In library" next to
    /// a search or discover result.
    /// </summary>
    public class LibraryStatusService : ILibraryStatusService
    {
        // A client asks about one screen of results at a time.
        public const int MaxIds = 200;

        private readonly ISeriesService _seriesService;
        private readonly ISeriesStatisticsService _seriesStatisticsService;
        private readonly IMovieService _movieService;
        private readonly IQueueService _queueService;

        public LibraryStatusService(ISeriesService seriesService,
                                    ISeriesStatisticsService seriesStatisticsService,
                                    IMovieService movieService,
                                    IQueueService queueService)
        {
            _seriesService = seriesService;
            _seriesStatisticsService = seriesStatisticsService;
            _movieService = movieService;
            _queueService = queueService;
        }

        public List<LibraryItemState> GetStatus(IEnumerable<int> tvdbIds, IEnumerable<int> tmdbIds)
        {
            var seriesIds = (tvdbIds ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct().Take(MaxIds).ToList();
            var movieIds = (tmdbIds ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct().Take(MaxIds).ToList();
            var result = new List<LibraryItemState>();

            if (seriesIds.Count == 0 && movieIds.Count == 0)
            {
                return result;
            }

            var queue = _queueService.GetQueue().Where(q => q.IsTrackable).ToList();
            var downloadingSeries = queue.Where(q => q.Series != null).Select(q => q.Series.Id).ToHashSet();
            var downloadingMovies = queue.Where(q => q.Movie != null).Select(q => q.Movie.Id).ToHashSet();

            foreach (var tvdbId in seriesIds)
            {
                var series = _seriesService.FindByTvdbId(tvdbId);
                var state = new LibraryItemState { MediaType = RequestMediaType.Series, ExternalId = tvdbId };

                if (series == null)
                {
                    state.Status = LibraryItemStatus.NotInLibrary;
                }
                else
                {
                    state.Id = series.Id;
                    state.Status = SeriesStatus(series, downloadingSeries.Contains(series.Id));
                }

                result.Add(state);
            }

            foreach (var tmdbId in movieIds)
            {
                var movie = _movieService.FindByTmdbId(tmdbId);
                var state = new LibraryItemState { MediaType = RequestMediaType.Movie, ExternalId = tmdbId };

                if (movie == null)
                {
                    state.Status = LibraryItemStatus.NotInLibrary;
                }
                else
                {
                    state.Id = movie.Id;
                    state.Status = MovieStatus(movie, downloadingMovies.Contains(movie.Id));
                }

                result.Add(state);
            }

            return result;
        }

        public static LibraryItemStatus MovieStatus(Movie movie, bool downloading)
        {
            if (downloading)
            {
                return LibraryItemStatus.Downloading;
            }

            if (movie.HasFile)
            {
                return LibraryItemStatus.Available;
            }

            return movie.Monitored ? LibraryItemStatus.Wanted : LibraryItemStatus.Unmonitored;
        }

        private LibraryItemStatus SeriesStatus(Series series, bool downloading)
        {
            if (downloading)
            {
                return LibraryItemStatus.Downloading;
            }

            var stats = _seriesStatisticsService.SeriesStatistics(series.Id, series.QualityProfileId);

            return SeriesStatus(series.Monitored, stats?.EpisodeFileCount ?? 0, stats?.EpisodeCount ?? 0);
        }

        // EpisodeCount is the monitored episodes that have aired or already have a file.
        public static LibraryItemStatus SeriesStatus(bool monitored, int episodeFileCount, int episodeCount)
        {
            if (episodeFileCount == 0)
            {
                return monitored ? LibraryItemStatus.Wanted : LibraryItemStatus.Unmonitored;
            }

            return episodeFileCount >= episodeCount ? LibraryItemStatus.Available : LibraryItemStatus.Partial;
        }
    }
}
