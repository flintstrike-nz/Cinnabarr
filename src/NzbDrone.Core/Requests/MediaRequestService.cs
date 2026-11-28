using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Requests
{
    public enum RequestMediaType
    {
        Series,
        Movie
    }

    /// <summary>
    /// "Add this item": the one request contract used by outside callers (the Jellyfin plugin, the remote request
    /// relay). Only the id is required; everything else falls back to a sensible default.
    /// </summary>
    public class MediaRequest
    {
        public RequestMediaType MediaType { get; set; }

        // TVDB id for a series, TMDb id for a movie.
        public int ExternalId { get; set; }
        public int? QualityProfileId { get; set; }
        public string RootFolderPath { get; set; }

        // Start a search straight away. Defaults to true.
        public bool SearchNow { get; set; } = true;

        // Series only: which episodes to monitor. Defaults to all.
        public Tv.MonitorTypes? SeriesMonitor { get; set; }
        public HashSet<int> Tags { get; set; }
    }

    public class MediaRequestResult
    {
        public RequestMediaType MediaType { get; set; }
        public int ExternalId { get; set; }

        // The id of the series or movie in Cinnabarr.
        public int Id { get; set; }
        public string Title { get; set; }

        // True when the item was already in the library; nothing was changed.
        public bool AlreadyExisted { get; set; }
    }

    public interface IMediaRequestService
    {
        MediaRequestResult Request(MediaRequest request);
    }

    public class MediaRequestService : IMediaRequestService
    {
        private readonly ISeriesService _seriesService;
        private readonly IAddSeriesService _addSeriesService;
        private readonly IMovieService _movieService;
        private readonly IAddMovieService _addMovieService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public MediaRequestService(ISeriesService seriesService,
                                   IAddSeriesService addSeriesService,
                                   IMovieService movieService,
                                   IAddMovieService addMovieService,
                                   IRootFolderService rootFolderService,
                                   IQualityProfileService qualityProfileService,
                                   IConfigService configService,
                                   Logger logger)
        {
            _seriesService = seriesService;
            _addSeriesService = addSeriesService;
            _movieService = movieService;
            _addMovieService = addMovieService;
            _rootFolderService = rootFolderService;
            _qualityProfileService = qualityProfileService;
            _configService = configService;
            _logger = logger;
        }

        public MediaRequestResult Request(MediaRequest request)
        {
            if (request == null || request.ExternalId <= 0)
            {
                throw Invalid("ExternalId", "An id greater than 0 is required.");
            }

            return request.MediaType == RequestMediaType.Movie ? RequestMovie(request) : RequestSeries(request);
        }

        private MediaRequestResult RequestSeries(MediaRequest request)
        {
            var existing = _seriesService.FindByTvdbId(request.ExternalId);

            if (existing != null)
            {
                return Result(request, existing.Id, existing.Title, true);
            }

            var monitor = request.SeriesMonitor ?? Tv.MonitorTypes.All;

            var series = new Series
            {
                TvdbId = request.ExternalId,
                Monitored = monitor != Tv.MonitorTypes.None,
                MonitorNewItems = NewItemMonitorTypes.All,
                RootFolderPath = PickRootFolder(request.RootFolderPath, LibraryType.Series),
                QualityProfileId = PickQualityProfile(request.QualityProfileId),
                SeriesType = SeriesTypes.Standard,
                SeasonType = SeasonType.Official,
                Language = Language.FindById(_configService.PreferredMetadataLanguage) ?? Language.English,
                SeasonFolder = true,
                Tags = request.Tags ?? new HashSet<int>(),
                AddOptions = new AddSeriesOptions
                {
                    Monitor = monitor,
                    SearchForMissingEpisodes = request.SearchNow
                }
            };

            var added = _addSeriesService.AddSeries(series);
            _logger.Info("Requested series {0} (TVDB {1})", added.Title, added.TvdbId);

            return Result(request, added.Id, added.Title, false);
        }

        private MediaRequestResult RequestMovie(MediaRequest request)
        {
            var existing = _movieService.FindByTmdbId(request.ExternalId);

            if (existing != null)
            {
                return Result(request, existing.Id, existing.Title, true);
            }

            var movie = new Movie
            {
                TmdbId = request.ExternalId,
                Monitored = true,
                MinimumAvailability = MovieStatusType.Released,
                RootFolderPath = PickRootFolder(request.RootFolderPath, LibraryType.Movies),
                QualityProfileId = PickQualityProfile(request.QualityProfileId),
                Tags = request.Tags ?? new HashSet<int>(),
                AddOptions = new AddMovieOptions
                {
                    Monitor = Movies.MonitorTypes.MovieOnly,
                    SearchForMovie = request.SearchNow,
                    AddMethod = AddMovieMethod.Manual
                }
            };

            var added = _addMovieService.AddMovie(movie);
            _logger.Info("Requested movie {0} (TMDb {1})", added.Title, added.TmdbId);

            return Result(request, added.Id, added.Title, false);
        }

        private string PickRootFolder(string requested, LibraryType library)
        {
            var folders = _rootFolderService.All();

            if (!string.IsNullOrWhiteSpace(requested))
            {
                var match = folders.FirstOrDefault(f => string.Equals(f.Path.TrimEnd('/', '\\'), requested.TrimEnd('/', '\\'), StringComparison.Ordinal));

                if (match == null || (match.Library != library && match.Library != LibraryType.Any))
                {
                    throw Invalid("RootFolderPath", $"'{requested}' is not a root folder for {Describe(library)}.");
                }

                return match.Path;
            }

            // A root folder set aside for this library wins over one that takes anything.
            var best = folders.FirstOrDefault(f => f.Library == library) ?? folders.FirstOrDefault(f => f.Library == LibraryType.Any);

            if (best == null)
            {
                throw Invalid("RootFolderPath", $"There is no root folder for {Describe(library)}. Add one in Settings → Media Management.");
            }

            return best.Path;
        }

        private int PickQualityProfile(int? requested)
        {
            var profiles = _qualityProfileService.All();

            if (requested.HasValue)
            {
                if (profiles.All(p => p.Id != requested.Value))
                {
                    throw Invalid("QualityProfileId", $"There is no quality profile with id {requested.Value}.");
                }

                return requested.Value;
            }

            if (profiles.Count == 0)
            {
                throw Invalid("QualityProfileId", "There are no quality profiles.");
            }

            return profiles.OrderBy(p => p.Id).First().Id;
        }

        private static string Describe(LibraryType library)
        {
            return library == LibraryType.Movies ? "movies" : "series";
        }

        private static MediaRequestResult Result(MediaRequest request, int id, string title, bool alreadyExisted)
        {
            return new MediaRequestResult
            {
                MediaType = request.MediaType,
                ExternalId = request.ExternalId,
                Id = id,
                Title = title,
                AlreadyExisted = alreadyExisted
            };
        }

        private static ValidationException Invalid(string property, string message)
        {
            return new ValidationException(new List<ValidationFailure> { new ValidationFailure(property, message) });
        }
    }
}
