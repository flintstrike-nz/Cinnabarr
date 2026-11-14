using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Games
{
    public class IgdbGame
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("summary")]
        public string Summary { get; set; }

        // Unix seconds.
        [JsonPropertyName("first_release_date")]
        public long? FirstReleaseDate { get; set; }

        [JsonPropertyName("rating")]
        public double? Rating { get; set; }

        [JsonPropertyName("genres")]
        public List<IgdbName> Genres { get; set; }

        [JsonPropertyName("cover")]
        public IgdbCover Cover { get; set; }

        [JsonPropertyName("involved_companies")]
        public List<IgdbInvolvedCompany> InvolvedCompanies { get; set; }
    }

    public class IgdbName
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class IgdbCover
    {
        [JsonPropertyName("image_id")]
        public string ImageId { get; set; }
    }

    public class IgdbInvolvedCompany
    {
        [JsonPropertyName("developer")]
        public bool Developer { get; set; }

        [JsonPropertyName("company")]
        public IgdbName Company { get; set; }
    }

    public class TwitchToken
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    public interface IIgdbClient
    {
        bool IsConfigured { get; }
        List<IgdbGame> Search(string title, string platformKey);
        string TestConnection();
    }

    /// <summary>IGDB (through a Twitch application): searches games by name on one platform.</summary>
    public class IgdbClient : IIgdbClient
    {
        // IGDB's platform ids.
        public static readonly IReadOnlyDictionary<string, int> PlatformIds = new Dictionary<string, int>
        {
            { "sms", 64 },
            { "snes", 19 },
            { "psx", 7 },
            { "ps2", 8 }
        };

        private const string Fields = "name,summary,first_release_date,rating,genres.name,cover.image_id,involved_companies.developer,involved_companies.company.name";

        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly ICached<string> _tokenCache;
        private readonly Logger _logger;

        public IgdbClient(IHttpClient httpClient, IConfigService configService, ICacheManager cacheManager, Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _logger = logger;
            _tokenCache = cacheManager.GetCache<string>(GetType());
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_configService.IgdbClientId) && !string.IsNullOrWhiteSpace(_configService.IgdbClientSecret);

        public List<IgdbGame> Search(string title, string platformKey)
        {
            var body = $"search \"{title.Replace("\\", " ").Replace("\"", " ")}\"; fields {Fields};";

            if (PlatformIds.TryGetValue(platformKey, out var platform))
            {
                body += $" where platforms = ({platform});";
            }

            body += " limit 5;";

            var request = new HttpRequestBuilder("https://api.igdb.com/v4/games")
                .SetHeader("Client-ID", _configService.IgdbClientId)
                .SetHeader("Authorization", "Bearer " + GetToken())
                .Post()
                .Build();
            request.SetContent(body);

            return _httpClient.Post<List<IgdbGame>>(request).Resource ?? new List<IgdbGame>();
        }

        // Empty when it works, otherwise what is wrong.
        public string TestConnection()
        {
            if (!IsConfigured)
            {
                return "Enter a client id and a client secret.";
            }

            try
            {
                _tokenCache.Remove("token");
                GetToken();
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "IGDB test failed");
                return "Twitch did not accept these credentials.";
            }
        }

        private string GetToken()
        {
            return _tokenCache.Get("token", FetchToken, TimeSpan.FromHours(12));
        }

        private string FetchToken()
        {
            var request = new HttpRequestBuilder("https://id.twitch.tv/oauth2/token")
                .AddQueryParam("client_id", _configService.IgdbClientId)
                .AddQueryParam("client_secret", _configService.IgdbClientSecret)
                .AddQueryParam("grant_type", "client_credentials")
                .Post()
                .Build();

            return _httpClient.Post<TwitchToken>(request).Resource.AccessToken;
        }
    }

    public static class IgdbMatcher
    {
        /// <summary>The result with the same name as the title, else the first (IGDB ranks its search results).</summary>
        public static IgdbGame PickBest(string title, IEnumerable<IgdbGame> results)
        {
            var list = results?.ToList() ?? new List<IgdbGame>();
            var wanted = Normalise(title);

            return list.FirstOrDefault(r => Normalise(r.Name) == wanted) ?? list.FirstOrDefault();
        }

        public static string Normalise(string name)
        {
            return new string((name ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        public static string CoverUrl(IgdbGame game)
        {
            return string.IsNullOrEmpty(game?.Cover?.ImageId) ? null : $"https://images.igdb.com/igdb/image/upload/t_cover_big/{game.Cover.ImageId}.jpg";
        }
    }
}
