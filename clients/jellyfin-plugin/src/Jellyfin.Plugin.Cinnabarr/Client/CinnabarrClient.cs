using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Cinnabarr.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Cinnabarr.Client
{
    public sealed class CinnabarrStatus
    {
        public bool Ok { get; set; }
        public string? Message { get; set; }
        public string? AppName { get; set; }
        public string? Version { get; set; }
    }

    public interface ICinnabarrClient
    {
        // Calls Cinnabarr's /api/v5/<path> with the API key. path is relative, e.g. "request/status?tmdbIds=603".
        Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken);

        // Checks a URL and key (the saved ones when both are empty).
        Task<CinnabarrStatus> TestAsync(string? url, string? apiKey, CancellationToken cancellationToken);
    }

    public class CinnabarrClient : ICinnabarrClient
    {
        public const string HttpClientName = "Cinnabarr";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Func<PluginConfiguration> _configuration;
        private readonly ILogger<CinnabarrClient> _logger;

        public CinnabarrClient(IHttpClientFactory httpClientFactory, ILogger<CinnabarrClient> logger)
            : this(httpClientFactory, () => Plugin.Instance?.Configuration ?? new PluginConfiguration(), logger)
        {
        }

        internal CinnabarrClient(IHttpClientFactory httpClientFactory, Func<PluginConfiguration> configuration, ILogger<CinnabarrClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
        {
            var config = _configuration();

            return SendAsync(config.CinnabarrUrl, config.ApiKey, method, path, content, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        public async Task<CinnabarrStatus> TestAsync(string? url, string? apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(apiKey))
            {
                var config = _configuration();
                url = config.CinnabarrUrl;
                apiKey = config.ApiKey;
            }

            try
            {
                using var response = await SendAsync(url, apiKey, HttpMethod.Get, "system/status", null, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return new CinnabarrStatus { Message = "Cinnabarr refused the API key." };
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new CinnabarrStatus { Message = $"Cinnabarr answered {(int)response.StatusCode} {response.ReasonPhrase}." };
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var json = JsonDocument.Parse(body);
                var root = json.RootElement;

                return new CinnabarrStatus
                {
                    Ok = true,
                    AppName = root.TryGetProperty("appName", out var app) ? app.GetString() : null,
                    Version = root.TryGetProperty("version", out var version) ? version.GetString() : null,
                    Message = "Connected."
                };
            }
            catch (ArgumentException e)
            {
                return new CinnabarrStatus { Message = e.Message };
            }
            catch (HttpRequestException e)
            {
                _logger.LogInformation(e, "Could not reach Cinnabarr");
                return new CinnabarrStatus { Message = "Could not reach Cinnabarr: " + e.Message };
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new CinnabarrStatus { Message = "Cinnabarr did not answer in time." };
            }
            catch (JsonException)
            {
                return new CinnabarrStatus { Message = "That address answered, but not like Cinnabarr does." };
            }
        }

        // The full URL for a call, or an ArgumentException that says what is wrong with the settings or path.
        public static Uri BuildUri(string? baseUrl, string path)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("The Cinnabarr URL is not set.");
            }

            if (!Uri.TryCreate(baseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var root) ||
                (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(root.Query) || !string.IsNullOrEmpty(root.Fragment))
            {
                throw new ArgumentException("The Cinnabarr URL must be an http:// or https:// address, for example http://localhost:8989.");
            }

            // Only plain relative paths under /api/v5: no scheme, no leading slash, no dot segments.
            if (string.IsNullOrWhiteSpace(path) || path.Contains("://", StringComparison.Ordinal) ||
                path.StartsWith('/') || path.Contains('\\', StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid Cinnabarr path.");
            }

            var pathOnly = path.Split('?', 2)[0];

            foreach (var segment in pathOnly.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.Contains('%', StringComparison.Ordinal))
                {
                    throw new ArgumentException("Invalid Cinnabarr path.");
                }
            }

            return new Uri(root, "api/v5/" + path);
        }

        private async Task<HttpResponseMessage> SendAsync(string? baseUrl, string? apiKey, HttpMethod method, string path, HttpContent? content, HttpCompletionOption completion, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("The Cinnabarr API key is not set.");
            }

            var uri = BuildUri(baseUrl, path);

            using var request = new HttpRequestMessage(method, uri) { Content = content };
            request.Headers.Add("X-Api-Key", apiKey.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var client = _httpClientFactory.CreateClient(HttpClientName);

            return await client.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);
        }
    }
}
