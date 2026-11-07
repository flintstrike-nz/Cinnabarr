using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.ImportLists.Movies
{
    // A JSON array of movies at a URL: [{ "title": "...", "year": 1999, "tmdb_id": 603, "imdb_id": "tt0133093" }].
    public class MovieCustomImport : MovieImportListBase<MovieCustomSettings>
    {
        private readonly IHttpClient _httpClient;

        public MovieCustomImport(IHttpClient httpClient,
                                 IImportListStatusService importListStatusService,
                                 IConfigService configService,
                                 IParsingService parsingService,
                                 ILocalizationService localizationService,
                                 Logger logger)
            : base(importListStatusService, configService, parsingService, localizationService, logger)
        {
            _httpClient = httpClient;
        }

        public override string Name => _localizationService.GetLocalizedString("ImportListsMovieCustomListSettingsName");

        public override ImportListType ListType => ImportListType.Advanced;

        protected override IEnumerable<ImportListMovieInfo> FetchMovies()
        {
            if (Settings.BaseUrl.IsNullOrWhiteSpace())
            {
                return Enumerable.Empty<ImportListMovieInfo>();
            }

            var request = new HttpRequestBuilder(Settings.BaseUrl.TrimEnd('/')).Accept(HttpAccept.Json).AllowRedirect().Build();
            var response = _httpClient.Get(request);

            return Parse(response.Content);
        }

        public static List<ImportListMovieInfo> Parse(string json)
        {
            var items = JsonConvert.DeserializeObject<List<CustomMovie>>(json) ?? new List<CustomMovie>();

            return items.Select(i =>
            {
                var tmdbId = i.TmdbId != 0 ? i.TmdbId : i.TmdbIdCamel;

                return new ImportListMovieInfo
                {
                    Title = i.Title.IsNullOrWhiteSpace() ? $"TmdbId: {tmdbId}" : i.Title,
                    Year = i.Year,
                    TmdbId = tmdbId,
                    ImdbId = i.ImdbId.IsNotNullOrWhiteSpace() ? i.ImdbId : i.ImdbIdCamel
                };
            }).ToList();
        }

        private class CustomMovie
        {
            public string Title { get; set; }
            public int Year { get; set; }

            [JsonProperty("tmdb_id")]
            public int TmdbId { get; set; }

            [JsonProperty("tmdbId")]
            public int TmdbIdCamel { get; set; }

            [JsonProperty("imdb_id")]
            public string ImdbId { get; set; }

            [JsonProperty("imdbId")]
            public string ImdbIdCamel { get; set; }
        }
    }
}
