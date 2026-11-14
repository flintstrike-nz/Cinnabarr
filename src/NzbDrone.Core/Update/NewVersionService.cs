using System;
using System.Text.Json.Serialization;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Update
{
    public class NewVersionInfo
    {
        public bool Available { get; set; }
        public string Current { get; set; }
        public string Latest { get; set; }
        public string Message { get; set; }
        public string Url { get; set; }
    }

    public interface INewVersionService
    {
        NewVersionInfo Check();
    }

    /// <summary>
    /// Tells the user when the branch their install was built from has moved on. The image is built with the commit it
    /// came from (CINNABARR_COMMIT); this asks GitHub for the newest commit of the branch. A build without a commit
    /// (a developer's own) is never told to update, and a failed check says nothing.
    /// </summary>
    public class NewVersionService : INewVersionService
    {
        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly Logger _logger;
        private readonly ICached<NewVersionInfo> _cache;

        public NewVersionService(IHttpClient httpClient, IConfigService configService, ICacheManager cacheManager, Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _logger = logger;
            _cache = cacheManager.GetCache<NewVersionInfo>(GetType());
        }

        public NewVersionInfo Check()
        {
            var current = Environment.GetEnvironmentVariable("CINNABARR_COMMIT");

            if (!_configService.UpdateCheckEnabled || string.IsNullOrWhiteSpace(current) || current == "unknown")
            {
                return new NewVersionInfo { Current = current };
            }

            return _cache.Get(current, () => Fetch(current), TimeSpan.FromHours(6));
        }

        private NewVersionInfo Fetch(string current)
        {
            var info = new NewVersionInfo { Current = current };

            try
            {
                var repo = _configService.UpdateCheckRepository;
                var branch = _configService.UpdateCheckBranch;
                var request = new HttpRequestBuilder($"https://api.github.com/repos/{repo}/commits/{branch}")
                    .SetHeader("Accept", "application/vnd.github+json")
                    .Build();
                var commit = _httpClient.Get<GitHubCommit>(request).Resource;

                info.Latest = commit.Sha;
                info.Message = commit.Commit?.Message?.Split('\n')[0];
                info.Url = commit.HtmlUrl;
                info.Available = !string.IsNullOrEmpty(commit.Sha) && !commit.Sha.StartsWith(current, StringComparison.OrdinalIgnoreCase) && !current.StartsWith(commit.Sha, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                // A private repository, no network, a rate limit: no banner, no error.
                _logger.Debug(ex, "Could not check for a newer version");
            }

            return info;
        }

        private class GitHubCommit
        {
            [JsonPropertyName("sha")]
            public string Sha { get; set; }

            [JsonPropertyName("html_url")]
            public string HtmlUrl { get; set; }

            [JsonPropertyName("commit")]
            public GitHubCommitDetail Commit { get; set; }
        }

        private class GitHubCommitDetail
        {
            [JsonPropertyName("message")]
            public string Message { get; set; }
        }
    }
}
