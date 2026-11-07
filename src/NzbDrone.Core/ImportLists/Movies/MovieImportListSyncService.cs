using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.ImportLists.Movies
{
    public interface IProcessMovieImportListItems
    {
        int Process(List<ImportListMovieInfo> items);
    }

    // Adds the movies that import lists offer and the library does not have yet. The list's own settings
    // (root folder, quality profile, monitor, search, tags) are used, as for series.
    public class MovieImportListSyncService : IProcessMovieImportListItems
    {
        private readonly IImportListFactory _importListFactory;
        private readonly IMovieService _movieService;
        private readonly IAddMovieService _addMovieService;
        private readonly IProvideMovieInfo _movieInfo;
        private readonly Logger _logger;

        public MovieImportListSyncService(IImportListFactory importListFactory,
                                          IMovieService movieService,
                                          IAddMovieService addMovieService,
                                          IProvideMovieInfo movieInfo,
                                          Logger logger)
        {
            _importListFactory = importListFactory;
            _movieService = movieService;
            _addMovieService = addMovieService;
            _movieInfo = movieInfo;
            _logger = logger;
        }

        public int Process(List<ImportListMovieInfo> items)
        {
            if (items.Empty())
            {
                return 0;
            }

            var importLists = _importListFactory.All().ToDictionary(l => l.Id);
            var pending = new Dictionary<int, Movie>();
            var toTag = new Dictionary<int, HashSet<int>>();

            foreach (var item in items)
            {
                if (!importLists.TryGetValue(item.ImportListId, out var importList) || !importList.EnableAutomaticAdd)
                {
                    continue;
                }

                if (item.TmdbId <= 0 && item.ImdbId.IsNotNullOrWhiteSpace())
                {
                    item.TmdbId = LookupTmdbId(item.ImdbId);
                }

                if (item.TmdbId <= 0)
                {
                    _logger.Debug("[{0}] Rejected, unable to find a TMDb ID", item.Title);
                    continue;
                }

                var existing = _movieService.FindByTmdbId(item.TmdbId);

                if (existing != null)
                {
                    if (importList.TagExisting != TagExistingType.None && importList.Tags.Any())
                    {
                        if (!toTag.TryGetValue(existing.Id, out var tags))
                        {
                            tags = new HashSet<int>();
                            toTag.Add(existing.Id, tags);
                        }

                        tags.UnionWith(importList.Tags);
                    }

                    _logger.Debug("{0} [{1}] Rejected, movie exists in database", item.TmdbId, item.Title);
                    continue;
                }

                if (pending.TryGetValue(item.TmdbId, out var pendingMovie))
                {
                    pendingMovie.Tags.UnionWith(importList.Tags);
                    continue;
                }

                var monitored = importList.ShouldMonitor != Tv.MonitorTypes.None;

                pending.Add(item.TmdbId, new Movie
                {
                    TmdbId = item.TmdbId,
                    ImdbId = item.ImdbId,
                    Title = item.Title,
                    Year = item.Year,
                    Monitored = monitored,
                    MinimumAvailability = MovieStatusType.Released,
                    RootFolderPath = importList.RootFolderPath,
                    QualityProfileId = importList.QualityProfileId,
                    Tags = new HashSet<int>(importList.Tags),
                    AddOptions = new AddMovieOptions
                    {
                        Monitor = monitored ? MonitorTypes.MovieOnly : MonitorTypes.None,
                        SearchForMovie = importList.SearchForMissingEpisodes,
                        AddMethod = AddMovieMethod.List
                    }
                });
            }

            var added = pending.Count > 0 ? _addMovieService.AddMovies(pending.Values.ToList(), true).Count : 0;

            TagExisting(toTag);

            _logger.ProgressInfo("Import List movies: {0} found, {1} added", items.Count, added);

            return added;
        }

        private int LookupTmdbId(string imdbId)
        {
            try
            {
                return _movieInfo.GetMovieByImdbId(imdbId)?.TmdbId ?? 0;
            }
            catch (MovieNotFoundException)
            {
                return 0;
            }
        }

        private void TagExisting(Dictionary<int, HashSet<int>> toTag)
        {
            foreach (var (movieId, tags) in toTag)
            {
                var movie = _movieService.GetMovie(movieId);
                var before = movie.Tags.Count;

                movie.Tags.UnionWith(tags);

                if (movie.Tags.Count != before)
                {
                    _logger.Debug("{0} tagged existing movie", movie);
                    _movieService.UpdateMovie(movie);
                }
            }
        }
    }
}
