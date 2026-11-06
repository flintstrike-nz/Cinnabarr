using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.RadarrMetadata.Resource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Movies.Translations;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MetadataSource.RadarrMetadata
{
    /// <summary>Movie information from the Radarr project's metadata service (api.radarr.video), which in turn reads TMDb.</summary>
    public class RadarrMetadataProxy : IProvideMovieInfo, ISearchForNewMovie
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        private readonly IConfigService _configService;
        private readonly IMovieService _movieService;

        public RadarrMetadataProxy(IHttpClient httpClient,
            IConfigService configService,
            IMovieService movieService,
            Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _movieService = movieService;

            _logger = logger;
        }

        // Read each time so a changed setting takes effect without a restart.
        private IHttpRequestBuilderFactory RadarrMetadata => new HttpRequestBuilder(_configService.MovieMetadataUrl).CreateFactory();

        public HashSet<int> GetChangedMovies(DateTime startTime)
        {
            // Round down to the hour to ensure we cover gap and don't kill cache every call
            var cacheAdjustedStart = startTime.AddMinutes(-15);
            var startDate = cacheAdjustedStart.Date.AddHours(cacheAdjustedStart.Hour).ToString("s");

            var request = RadarrMetadata.Create()
                .SetSegment("route", "movie/changed")
                .AddQueryParam("since", startDate)
                .Build();

            request.AllowAutoRedirect = true;
            request.SuppressHttpError = true;

            var response = _httpClient.Get<List<int>>(request);

            return new HashSet<int>(response.Resource);
        }

        public List<MovieMetadata> GetTrendingMovies()
        {
            var request = RadarrMetadata.Create()
                .SetSegment("route", "list/tmdb/trending")
                .Build();

            request.AllowAutoRedirect = true;
            request.SuppressHttpError = true;

            var response = _httpClient.Get<List<MovieResource>>(request);

            return response.Resource.DistinctBy(x => x.TmdbId).Select(MapMovie).ToList();
        }

        public List<MovieMetadata> GetPopularMovies()
        {
            var request = RadarrMetadata.Create()
                .SetSegment("route", "list/tmdb/popular")
                .Build();

            request.AllowAutoRedirect = true;
            request.SuppressHttpError = true;

            var response = _httpClient.Get<List<MovieResource>>(request);

            return response.Resource.DistinctBy(x => x.TmdbId).Select(MapMovie).ToList();
        }

        public Tuple<MovieMetadata, List<Credit>> GetMovieInfo(int tmdbId)
        {
            var httpRequest = RadarrMetadata.Create()
                                             .SetSegment("route", "movie")
                                             .Resource(tmdbId.ToString())
                                             .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<MovieResource>(httpRequest);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MovieNotFoundException(tmdbId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            var credits = new List<Credit>();
            credits.AddRange(httpResponse.Resource.Credits.Cast.Select(MapCast));
            credits.AddRange(httpResponse.Resource.Credits.Crew.Select(MapCrew));

            var movie = MapMovie(httpResponse.Resource);

            return new Tuple<MovieMetadata, List<Credit>>(movie, credits.ToList());
        }

        public List<MovieMetadata> GetBulkMovieInfo(List<int> tmdbIds)
        {
            var httpRequest = RadarrMetadata.Create()
                                             .SetSegment("route", "movie/bulk")
                                             .Build();

            httpRequest.Headers.ContentType = "application/json";

            httpRequest.SetContent(tmdbIds.ToJson());
            httpRequest.ContentSummary = tmdbIds.ToJson(Formatting.None);

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Post<List<MovieResource>>(httpRequest);

            if (httpResponse.HasHttpError || httpResponse.Resource.Count == 0)
            {
                throw new HttpException(httpRequest, httpResponse);
            }

            var movies = httpResponse.Resource.Select(MapMovie).ToList();

            return movies;
        }

        public MovieMetadata GetMovieByImdbId(string imdbId)
        {
            imdbId = Parser.Parser.NormalizeImdbId(imdbId);

            if (imdbId == null)
            {
                return null;
            }

            var httpRequest = RadarrMetadata.Create()
                                             .SetSegment("route", "movie/imdb")
                                             .Resource(imdbId.ToString())
                                             .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<List<MovieResource>>(httpRequest);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MovieNotFoundException(imdbId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            var movie = httpResponse.Resource.SelectList(MapMovie).FirstOrDefault();

            return movie;
        }

        public List<Movie> SearchForNewMovie(string title)
        {
            try
            {
                var imdbUrl = Regex.Match(title, @"\bimdb\.com/title/(tt\d{7,})\b", RegexOptions.IgnoreCase);
                var tmdbUrl = Regex.Match(title, @"\bthemoviedb\.org/movie/(\d+)\b", RegexOptions.IgnoreCase);

                if (imdbUrl.Success)
                {
                    title = "imdb:" + imdbUrl.Groups[1].Value;
                }
                else if (tmdbUrl.Success)
                {
                    title = "tmdb:" + tmdbUrl.Groups[1].Value;
                }

                var lowerTitle = title.ToLower().Replace(".", string.Empty);

                if (lowerTitle.StartsWith("imdb:") || lowerTitle.StartsWith("imdbid:"))
                {
                    var imdbId = lowerTitle.Split(':')[1].Trim();

                    if (imdbId.IsNullOrWhiteSpace() || imdbId.Any(char.IsWhiteSpace))
                    {
                        return new List<Movie>();
                    }

                    return Single(() => GetMovieByImdbId(imdbId));
                }

                if (lowerTitle.StartsWith("tmdb:") || lowerTitle.StartsWith("tmdbid:"))
                {
                    var slug = lowerTitle.Split(':')[1].Trim();

                    if (!int.TryParse(slug, out var tmdbId))
                    {
                        return new List<Movie>();
                    }

                    return Single(() => GetMovieInfo(tmdbId).Item1);
                }

                // "The Matrix 1999" and "The Matrix (1999)" search for the title with the year.
                var parserTitle = lowerTitle;
                var yearTerm = string.Empty;
                var parsed = MovieParser.ParseMovieTitle(title, true);

                if (parsed != null && parsed.PrimaryMovieTitle != title)
                {
                    parserTitle = parsed.PrimaryMovieTitle.ToLower().Replace(".", " ");

                    if (parsed.Year > 1800)
                    {
                        yearTerm = parsed.Year.ToString();
                    }
                }

                var searchTerm = parserTitle.Replace("_", "+").Replace(" ", "+").Replace(".", "+");

                var request = RadarrMetadata.Create()
                    .SetSegment("route", "search")
                    .AddQueryParam("q", searchTerm)
                    .AddQueryParam("year", yearTerm)
                    .Build();

                var httpResponse = _httpClient.Get<List<MovieResource>>(request);

                return httpResponse.Resource.SelectList(MapSearchResult);
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex);
                throw new RadarrMetadataException("Search for '{0}' failed. Unable to communicate with the movie metadata service. {1}", ex, title, ex.Message);
            }
            catch (WebException ex)
            {
                _logger.Warn(ex);
                throw new RadarrMetadataException("Search for '{0}' failed. Unable to communicate with the movie metadata service. {1}", ex, title, ex.Message);
            }
            catch (Exception ex) when (ex is not RadarrMetadataException)
            {
                _logger.Warn(ex);
                throw new RadarrMetadataException("Search for '{0}' failed. Invalid response received from the movie metadata service. {1}", ex, title, ex.Message);
            }
        }

        private List<Movie> Single(Func<MovieMetadata> lookup)
        {
            try
            {
                var metadata = lookup();

                return metadata == null ? new List<Movie>() : new List<Movie> { _movieService.FindByTmdbId(metadata.TmdbId) ?? new Movie { MovieMetadata = metadata } };
            }
            catch (MovieNotFoundException)
            {
                return new List<Movie>();
            }
        }

        private Movie MapSearchResult(MovieResource result)
        {
            return _movieService.FindByTmdbId(result.TmdbId) ?? new Movie { MovieMetadata = MapMovie(result) };
        }

        public MovieMetadata MapMovie(MovieResource resource)
        {
            var movie = new MovieMetadata();
            var altTitles = new List<AlternativeTitle>();

            movie.TmdbId = resource.TmdbId;
            movie.ImdbId = resource.ImdbId;
            movie.Title = resource.Title;
            movie.OriginalTitle = resource.OriginalTitle;
            movie.CleanTitle = resource.Title.CleanMovieTitle();
            movie.SortTitle = MovieTitleNormalizer.Normalize(resource.Title, resource.TmdbId);
            movie.CleanOriginalTitle = resource.OriginalTitle.CleanMovieTitle();
            movie.Overview = resource.Overview;

            movie.AlternativeTitles.AddRange(resource.AlternativeTitles.Select(MapAlternativeTitle));

            movie.Translations.AddRange(resource.Translations.Select(MapTranslation));

            movie.OriginalLanguage = (resource.OriginalLanguage.IsNotNullOrWhiteSpace() ? IsoLanguages.Find(resource.OriginalLanguage.ToLower())?.Language : null) ?? Language.English;

            movie.Website = resource.Homepage;
            movie.InCinemas = resource.InCinema;
            movie.PhysicalRelease = resource.PhysicalRelease;
            movie.DigitalRelease = resource.DigitalRelease;

            movie.Year = resource.Year;

            // If the premier differs from the TMDB year, use it as a secondary year.
            if (resource.Premier.HasValue && resource.Premier?.Year != movie.Year)
            {
                movie.SecondaryYear = resource.Premier?.Year;
            }

            if (resource.Runtime != null)
            {
                movie.Runtime = resource.Runtime.Value;
            }

            if (resource.Popularity != null)
            {
                movie.Popularity = resource.Popularity.Value;
            }

            var certificationCountry = _configService.CertificationCountry.ToString();

            movie.Certification = resource.Certifications.FirstOrDefault(m => m.Country == certificationCountry)?.Certification;
            movie.Ratings = MapRatings(resource.MovieRatings) ?? new Ratings();

            movie.TmdbId = resource.TmdbId;
            movie.Genres = resource.Genres ?? new List<string>();
            movie.Keywords = resource.Keywords ?? new List<string>();
            movie.Images = resource.Images.Select(MapImage).ToList();

            movie.Recommendations = resource.Recommendations?.Select(r => r.TmdbId).ToList() ?? new List<int>();

            // Workaround due to metadata change until cache cleans up
            var tmdbRating = resource.Ratings.FirstOrDefault();

            if (movie.Ratings.Tmdb == null && tmdbRating != null)
            {
                movie.Ratings.Tmdb = new RatingChild
                {
                    Votes = tmdbRating.Count,
                    Value = tmdbRating.Value
                };
            }

            var now = DateTime.UtcNow;

            movie.Status = MovieStatusType.Announced;

            if (resource.InCinema.HasValue && now > resource.InCinema)
            {
                movie.Status = MovieStatusType.InCinemas;

                if (!resource.PhysicalRelease.HasValue && !resource.DigitalRelease.HasValue && now > resource.InCinema.Value.AddDays(90))
                {
                    movie.Status = MovieStatusType.Released;
                }
            }

            if (resource.PhysicalRelease.HasValue && now >= resource.PhysicalRelease)
            {
                movie.Status = MovieStatusType.Released;
            }

            if (resource.DigitalRelease.HasValue && now >= resource.DigitalRelease)
            {
                movie.Status = MovieStatusType.Released;
            }

            movie.YouTubeTrailerId = resource.YoutubeTrailerId;
            movie.Studio = resource.Studio;

            if (resource.Collection != null)
            {
                movie.CollectionTmdbId = resource.Collection.TmdbId;
                movie.CollectionTitle = resource.Collection.Name;
            }

            return movie;
        }

        private static Credit MapCast(CastResource arg)
        {
            var newActor = new Credit
            {
                Name = arg.Name,
                Character = arg.Character,
                Order = arg.Order,
                CreditTmdbId = arg.CreditId,
                PersonTmdbId = arg.TmdbId,
                Type = CreditType.Cast,
                Images = arg.Images.Select(MapImage).ToList()
            };

            return newActor;
        }

        private static Credit MapCrew(CrewResource arg)
        {
            var newActor = new Credit
            {
                Name = arg.Name,
                Department = arg.Department,
                Job = arg.Job,
                Order = arg.Order,
                CreditTmdbId = arg.CreditId,
                PersonTmdbId = arg.TmdbId,
                Type = CreditType.Crew,
                Images = arg.Images.Select(MapImage).ToList()
            };

            return newActor;
        }

        private static AlternativeTitle MapAlternativeTitle(AlternativeTitleResource arg)
        {
            var newAlternativeTitle = new AlternativeTitle
            {
                Title = arg.Title,
                SourceType = SourceType.Tmdb,
                CleanTitle = arg.Title.CleanMovieTitle()
            };

            return newAlternativeTitle;
        }

        private static MovieTranslation MapTranslation(TranslationResource arg)
        {
            var newAlternativeTitle = new MovieTranslation
            {
                Title = arg.Title,
                Overview = arg.Overview,
                CleanTitle = arg.Title.CleanMovieTitle(),
                Language = IsoLanguages.Find(arg.Language.ToLower())?.Language
            };

            return newAlternativeTitle;
        }

        private static Ratings MapRatings(RatingResource ratings)
        {
            if (ratings == null)
            {
                return new Ratings();
            }

            var mappedRatings = new Ratings();

            if (ratings.Tmdb != null)
            {
                mappedRatings.Tmdb = new RatingChild
                {
                    Type = (RatingType)Enum.Parse(typeof(RatingType), ratings.Tmdb.Type, true),
                    Value = ratings.Tmdb.Value,
                    Votes = ratings.Tmdb.Count
                };
            }

            if (ratings.Imdb != null)
            {
                mappedRatings.Imdb = new RatingChild
                {
                    Type = (RatingType)Enum.Parse(typeof(RatingType), ratings.Imdb.Type, true),
                    Value = ratings.Imdb.Value,
                    Votes = ratings.Imdb.Count
                };
            }

            if (ratings.Metacritic != null)
            {
                mappedRatings.Metacritic = new RatingChild
                {
                    Type = (RatingType)Enum.Parse(typeof(RatingType), ratings.Metacritic.Type, true),
                    Value = ratings.Metacritic.Value,
                    Votes = ratings.Metacritic.Count
                };
            }

            if (ratings.RottenTomatoes != null)
            {
                mappedRatings.RottenTomatoes = new RatingChild
                {
                    Type = (RatingType)Enum.Parse(typeof(RatingType), ratings.RottenTomatoes.Type, true),
                    Value = ratings.RottenTomatoes.Value,
                    Votes = ratings.RottenTomatoes.Count
                };
            }

            if (ratings.Trakt != null)
            {
                mappedRatings.Trakt = new RatingChild
                {
                    Type = (RatingType)Enum.Parse(typeof(RatingType), ratings.Trakt.Type, true),
                    Value = ratings.Trakt.Value,
                    Votes = ratings.Trakt.Count
                };
            }

            return mappedRatings;
        }

        private static MediaCover.MediaCover MapImage(ImageResource arg)
        {
            return new MediaCover.MediaCover
            {
                RemoteUrl = arg.Url,
                CoverType = MapCoverType(arg.CoverType)
            };
        }

        private static MediaCoverTypes MapCoverType(string coverType)
        {
            switch (coverType.ToLower())
            {
                case "poster":
                    return MediaCoverTypes.Poster;
                case "headshot":
                    return MediaCoverTypes.Headshot;
                case "fanart":
                    return MediaCoverTypes.Fanart;
                case "clearlogo":
                    return MediaCoverTypes.Clearlogo;
                default:
                    return MediaCoverTypes.Unknown;
            }
        }
    }
}
