using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Cinnabarr.Access;
using Jellyfin.Plugin.Cinnabarr.Client;
using Jellyfin.Plugin.Cinnabarr.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Cinnabarr.Api
{
    public sealed class MediaRequestBody
    {
        // "series" (TVDB id) or "movie" (TMDb id).
        public string MediaType { get; set; } = string.Empty;
        public int ExternalId { get; set; }
        public int? QualityProfileId { get; set; }
        public bool? SearchNow { get; set; }
        public string? SeriesMonitor { get; set; }
    }

    /// <summary>
    /// The Cinnabarr calls the apps may make, each checked against the caller's role. This is a fixed list: nothing
    /// here forwards a path the app chose. Answers are Cinnabarr's own (camelCase JSON) unless noted.
    /// </summary>
    [ApiController]
    [Route("Cinnabarr")]
    [Authorize]
    public partial class CinnabarrProxyController : ControllerBase
    {
        private const int MaxJsonBody = 256 * 1024;
        private const int MaxStateBody = 64 * 1024 * 1024;

        private readonly ICinnabarrClient _client;
        private readonly IRequestLog _requestLog;
        private readonly ILogger<CinnabarrProxyController> _logger;

        public CinnabarrProxyController(ICinnabarrClient client, IRequestLog requestLog, ILogger<CinnabarrProxyController> logger)
        {
            _client = client;
            _requestLog = requestLog;
            _logger = logger;
        }

        // ---- Library, lookup and requests (Requester) ----

        [HttpGet("Series")]
        public Task<IActionResult> AllSeries(CancellationToken ct) => Forward(CinnabarrRoles.Requester, HttpMethod.Get, "series", ct);

        [HttpGet("Series/{id:int}")]
        public Task<IActionResult> Series([FromRoute] int id, CancellationToken ct) => Forward(CinnabarrRoles.Requester, HttpMethod.Get, $"series/{id}", ct);

        [HttpGet("Movies")]
        public Task<IActionResult> AllMovies(CancellationToken ct) => Forward(CinnabarrRoles.Requester, HttpMethod.Get, "movie", ct);

        [HttpGet("Movies/{id:int}")]
        public Task<IActionResult> Movie([FromRoute] int id, CancellationToken ct) => Forward(CinnabarrRoles.Requester, HttpMethod.Get, $"movie/{id}", ct);

        // A series poster or fanart Cinnabarr keeps locally. Movie images come from their remote URLs.
        [HttpGet("Image/Series/{id:int}/{file}")]
        public Task<IActionResult> SeriesImage([FromRoute] int id, [FromRoute] string file, CancellationToken ct)
        {
            if (!ImageFile().IsMatch(file ?? string.Empty))
            {
                return Task.FromResult<IActionResult>(NotFound());
            }

            return Forward(CinnabarrRoles.Requester, HttpMethod.Get, $"mediacover/{id}/{file}", ct);
        }

        [HttpGet("Lookup/Series")]
        public Task<IActionResult> LookupSeries([FromQuery] string term, CancellationToken ct)
        {
            return string.IsNullOrWhiteSpace(term)
                ? Task.FromResult(Bad("Enter something to search for."))
                : Forward(CinnabarrRoles.Requester, HttpMethod.Get, "series/lookup" + Query(("term", term)), ct);
        }

        [HttpGet("Lookup/Movies")]
        public Task<IActionResult> LookupMovies([FromQuery] string term, CancellationToken ct)
        {
            return string.IsNullOrWhiteSpace(term)
                ? Task.FromResult(Bad("Enter something to search for."))
                : Forward(CinnabarrRoles.Requester, HttpMethod.Get, "movie/lookup" + Query(("term", term)), ct);
        }

        // Trending or popular movies, with the ones already in the library marked.
        [HttpGet("Discover/Movies")]
        public Task<IActionResult> DiscoverMovies([FromQuery] string? list, CancellationToken ct)
        {
            var which = string.Equals(list, "popular", StringComparison.OrdinalIgnoreCase) ? "popular" : "trending";

            return Forward(CinnabarrRoles.Requester, HttpMethod.Get, "movie/lookup/discover" + Query(("list", which)), ct);
        }

        // Whether items are in the library: comma-separated TVDB (series) and TMDb (movie) ids.
        [HttpGet("Status")]
        public Task<IActionResult> Status([FromQuery] string? tvdbIds, [FromQuery] string? tmdbIds, CancellationToken ct)
        {
            if (!IsIdList(tvdbIds) || !IsIdList(tmdbIds))
            {
                return Task.FromResult(Bad("Ids must be numbers separated by commas."));
            }

            return Forward(CinnabarrRoles.Requester, HttpMethod.Get, "request/status" + Query(("tvdbIds", tvdbIds), ("tmdbIds", tmdbIds)), ct);
        }

        // Adds a series or movie (or says it is already there) and records who asked.
        [HttpPost("Request")]
        [Consumes("application/json")]
        public async Task<IActionResult> AddRequest([FromBody] MediaRequestBody body, CancellationToken ct)
        {
            var denied = Check(CinnabarrRoles.Requester);

            if (denied != null)
            {
                return denied;
            }

            var mediaType = (body?.MediaType ?? string.Empty).ToLowerInvariant();

            if (body == null || (mediaType != "series" && mediaType != "movie") || body.ExternalId <= 0)
            {
                return Bad("A request needs mediaType (series or movie) and an externalId.");
            }

            var payload = JsonSerializer.Serialize(new
            {
                mediaType,
                externalId = body.ExternalId,
                qualityProfileId = body.QualityProfileId,
                searchNow = body.SearchNow,
                seriesMonitor = string.IsNullOrWhiteSpace(body.SeriesMonitor) ? null : body.SeriesMonitor
            });

            HttpResponseMessage response;

            try
            {
                response = await _client.SendAsync(HttpMethod.Post, "request", new StringContent(payload, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
            }
            catch (Exception e) when (IsUpstreamFailure(e, ct))
            {
                return UpstreamFailure(e);
            }

            using (response)
            {
                if (IsKeyRefused(response))
                {
                    return KeyRefused();
                }

                var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    LogRequest(mediaType, body.ExternalId, text);
                }

                return new ContentResult
                {
                    StatusCode = (int)response.StatusCode,
                    Content = text,
                    ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json"
                };
            }
        }

        // Who asked for what: everyone's for a manager, otherwise the caller's own.
        [HttpGet("Requests")]
        public IActionResult Requests([FromQuery] int count = 100)
        {
            var roles = Roles();

            if (!roles.HasFlag(CinnabarrRoles.Requester))
            {
                return Forbid();
            }

            Guid? only = roles.HasFlag(CinnabarrRoles.Manager) ? null : RoleResolver.GetUserId(User);

            return Ok(_requestLog.Recent(only, Math.Clamp(count, 1, 500)));
        }

        // ---- Downloads and interactive search (Manager) ----

        [HttpGet("Queue")]
        public Task<IActionResult> Queue([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        {
            var query = Query(
                ("page", Math.Max(1, page).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("pageSize", Math.Clamp(pageSize, 1, 200).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("includeSubresources", "series"),
                ("includeSubresources", "episodes"));

            return Forward(CinnabarrRoles.Manager, HttpMethod.Get, "queue" + query, ct);
        }

        // removalMethod: removeFromClient (default), ignore. blocklistMethod: doNotBlocklist (default), blocklistAndSearch, blocklistOnly.
        [HttpDelete("Queue/{id:int}")]
        public Task<IActionResult> RemoveFromQueue([FromRoute] int id, [FromQuery] string? removalMethod, [FromQuery] string? blocklistMethod, CancellationToken ct)
        {
            var removal = OneOf(removalMethod, "removeFromClient", "removeFromClient", "ignore");
            var blocklist = OneOf(blocklistMethod, "doNotBlocklist", "doNotBlocklist", "blocklistAndSearch", "blocklistOnly");

            if (removal == null || blocklist == null)
            {
                return Task.FromResult(Bad("Unknown removal or blocklist method."));
            }

            return Forward(CinnabarrRoles.Manager, HttpMethod.Delete, $"queue/{id}" + Query(("removalMethod", removal), ("blocklistMethod", blocklist)), ct);
        }

        // Grabs a release that is waiting (pending) in the queue.
        [HttpPost("Queue/{id:int}/Grab")]
        public Task<IActionResult> GrabPending([FromRoute] int id, CancellationToken ct) => Forward(CinnabarrRoles.Manager, HttpMethod.Post, $"queue/grab/{id}", ct);

        [HttpPost("Downloads/{downloadId}/Pause")]
        public Task<IActionResult> Pause([FromRoute] string downloadId, CancellationToken ct) => EngineAction(downloadId, "pause", ct);

        [HttpPost("Downloads/{downloadId}/Resume")]
        public Task<IActionResult> Resume([FromRoute] string downloadId, CancellationToken ct) => EngineAction(downloadId, "resume", ct);

        // Interactive search for a season (seriesId + seasonNumber) or an episode (episodeId).
        [HttpGet("Releases/Series")]
        public Task<IActionResult> SeriesReleases([FromQuery] int? seriesId, [FromQuery] int? seasonNumber, [FromQuery] int? episodeId, [FromQuery] int versionSlotId = 0, CancellationToken ct = default)
        {
            // Cinnabarr searches an episode, or a season of a series; anything else would be its RSS feed.
            if (episodeId == null && (seriesId == null || seasonNumber == null))
            {
                return Task.FromResult(Bad("Give episodeId, or seriesId with seasonNumber."));
            }

            var query = Query(
                ("seriesId", seriesId?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("seasonNumber", seasonNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("episodeId", episodeId?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("versionSlotId", versionSlotId.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            return Forward(CinnabarrRoles.Manager, HttpMethod.Get, "release" + query, ct);
        }

        [HttpPost("Releases/Series")]
        [Consumes("application/json")]
        public Task<IActionResult> GrabSeriesRelease(CancellationToken ct) => ForwardJsonBody(CinnabarrRoles.Manager, "release", ct);

        [HttpGet("Releases/Movie")]
        public Task<IActionResult> MovieReleases([FromQuery] int movieId, [FromQuery] int versionSlotId = 0, CancellationToken ct = default)
        {
            var query = Query(
                ("movieId", movieId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("versionSlotId", versionSlotId.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            return Forward(CinnabarrRoles.Manager, HttpMethod.Get, "release/movie" + query, ct);
        }

        [HttpPost("Releases/Movie")]
        [Consumes("application/json")]
        public Task<IActionResult> GrabMovieRelease(CancellationToken ct) => ForwardJsonBody(CinnabarrRoles.Manager, "release/movie", ct);

        // Free-text search across indexers, for releases that match nothing in the library yet.
        [HttpGet("Search")]
        public Task<IActionResult> Search([FromQuery] string query, CancellationToken ct)
        {
            return string.IsNullOrWhiteSpace(query)
                ? Task.FromResult(Bad("Enter something to search for."))
                : Forward(CinnabarrRoles.Manager, HttpMethod.Get, "search" + Query(("query", query)), ct);
        }

        [HttpPost("Search/Grab")]
        [Consumes("application/json")]
        public Task<IActionResult> GrabSearchResult(CancellationToken ct) => ForwardJsonBody(CinnabarrRoles.Manager, "search/grab", ct);

        // ---- Games (Player) ----

        [HttpGet("Games")]
        public Task<IActionResult> Games(CancellationToken ct) => Forward(CinnabarrRoles.Player, HttpMethod.Get, "game", ct);

        [HttpGet("Games/{id:int}")]
        public Task<IActionResult> Game([FromRoute] int id, CancellationToken ct) => Forward(CinnabarrRoles.Player, HttpMethod.Get, $"game/{id}", ct);

        [HttpGet("Games/{id:int}/Cover")]
        public Task<IActionResult> GameCover([FromRoute] int id, CancellationToken ct) => Forward(CinnabarrRoles.Player, HttpMethod.Get, $"game/{id}/cover", ct);

        [HttpGet("Games/{id:int}/Play")]
        public Task<IActionResult> GamePlayInfo([FromRoute] int id, CancellationToken ct) => Forward(CinnabarrRoles.Player, HttpMethod.Get, $"game/{id}/play", ct);

        // The ROM, with Range requests passed on so the emulator can fetch big discs in pieces.
        [HttpGet("Games/{id:int}/Rom")]
        public Task<IActionResult> GameRom([FromRoute] int id, CancellationToken ct)
        {
            return Forward(CinnabarrRoles.Player, HttpMethod.Get, $"game/{id}/rom", null, Request.Headers.Range.ToString(), ct);
        }

        [HttpGet("Games/{id:int}/State")]
        public Task<IActionResult> GameState([FromRoute] int id, CancellationToken ct) => Forward(CinnabarrRoles.Player, HttpMethod.Get, $"game/{id}/state", ct);

        [HttpPut("Games/{id:int}/State")]
        [RequestSizeLimit(MaxStateBody)]
        public async Task<IActionResult> SaveGameState([FromRoute] int id, CancellationToken ct)
        {
            var denied = Check(CinnabarrRoles.Player);

            if (denied != null)
            {
                return denied;
            }

            using var buffer = new MemoryStream();
            await Request.Body.CopyToAsync(buffer, ct).ConfigureAwait(false);

            if (buffer.Length == 0 || buffer.Length > MaxStateBody)
            {
                return Bad("The save state is empty or too big.");
            }

            buffer.Position = 0;
            var content = new StreamContent(buffer);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            return await Forward(CinnabarrRoles.Player, HttpMethod.Put, $"game/{id}/state", content, null, ct).ConfigureAwait(false);
        }

        [HttpGet("Games/Bios/{platformKey}")]
        public Task<IActionResult> GameBios([FromRoute] string platformKey, CancellationToken ct)
        {
            if (!PlatformKey().IsMatch(platformKey ?? string.Empty))
            {
                return Task.FromResult<IActionResult>(NotFound());
            }

            return Forward(CinnabarrRoles.Player, HttpMethod.Get, $"game/bios/{platformKey}", ct);
        }

        // ---- Helpers ----

        internal static string Query(params (string Name, string? Value)[] parameters)
        {
            var parts = parameters
                .Where(p => !string.IsNullOrEmpty(p.Value))
                .Select(p => Uri.EscapeDataString(p.Name) + "=" + Uri.EscapeDataString(p.Value!))
                .ToArray();

            return parts.Length == 0 ? string.Empty : "?" + string.Join("&", parts);
        }

        internal static bool IsIdList(string? ids)
        {
            return string.IsNullOrEmpty(ids) || IdList().IsMatch(ids);
        }

        // The allowed value (in Cinnabarr's spelling) for a case-insensitive input, the fallback when empty, or null.
        internal static string? OneOf(string? value, string fallback, params string[] allowed)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return allowed.FirstOrDefault(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));
        }

        [GeneratedRegex("^[0-9]{1,10}(,[0-9]{1,10}){0,199}$")]
        private static partial Regex IdList();

        [GeneratedRegex("^[a-z]{1,20}(-[0-9]{1,4})?\\.(jpg|png|gif)$")]
        private static partial Regex ImageFile();

        [GeneratedRegex("^[a-z0-9]{1,20}$")]
        private static partial Regex PlatformKey();

        [GeneratedRegex("^[A-Za-z0-9]{1,64}$")]
        private static partial Regex DownloadId();

        private Task<IActionResult> EngineAction(string downloadId, string action, CancellationToken ct)
        {
            if (!DownloadId().IsMatch(downloadId ?? string.Empty))
            {
                return Task.FromResult<IActionResult>(NotFound());
            }

            return Forward(CinnabarrRoles.Manager, HttpMethod.Post, $"engine/downloads/{downloadId}/{action}", ct);
        }

        private Task<IActionResult> Forward(CinnabarrRoles role, HttpMethod method, string path, CancellationToken ct)
        {
            return Forward(role, method, path, null, null, ct);
        }

        private async Task<IActionResult> Forward(CinnabarrRoles role, HttpMethod method, string path, HttpContent? content, string? range, CancellationToken ct)
        {
            var denied = Check(role);

            if (denied != null)
            {
                content?.Dispose();
                return denied;
            }

            try
            {
                var response = await _client.SendAsync(method, path, content, string.IsNullOrEmpty(range) ? null : range, ct).ConfigureAwait(false);

                if (IsKeyRefused(response))
                {
                    response.Dispose();
                    return KeyRefused();
                }

                return new UpstreamResult(response);
            }
            catch (Exception e) when (IsUpstreamFailure(e, ct))
            {
                return UpstreamFailure(e);
            }
        }

        // Passes a JSON body on unchanged, after checking it is JSON and not huge.
        private async Task<IActionResult> ForwardJsonBody(CinnabarrRoles role, string path, CancellationToken ct)
        {
            var denied = Check(role);

            if (denied != null)
            {
                return denied;
            }

            string text;

            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                var chars = new char[MaxJsonBody + 1];
                var read = await reader.ReadBlockAsync(chars.AsMemory(), ct).ConfigureAwait(false);

                if (read > MaxJsonBody)
                {
                    return Bad("The request is too big.");
                }

                text = new string(chars, 0, read);
            }

            try
            {
                using var json = JsonDocument.Parse(text);

                if (json.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return Bad("Expected a JSON object.");
                }
            }
            catch (JsonException)
            {
                return Bad("Expected a JSON object.");
            }

            return await Forward(role, HttpMethod.Post, path, new StringContent(text, Encoding.UTF8, "application/json"), null, ct).ConfigureAwait(false);
        }

        private CinnabarrRoles Roles() => RoleResolver.Resolve(User, Plugin.Instance?.Configuration);

        private IActionResult? Check(CinnabarrRoles role)
        {
            if (!Roles().HasFlag(role))
            {
                return Forbid();
            }

            if (!_client.IsConfigured)
            {
                return Message(StatusCodes.Status503ServiceUnavailable, "Cinnabarr is not set up in Jellyfin yet. An administrator can do that under Dashboard → Plugins → Cinnabarr.");
            }

            return null;
        }

        private static bool IsUpstreamFailure(Exception e, CancellationToken ct)
        {
            return e is ArgumentException || e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested);
        }

        private IActionResult UpstreamFailure(Exception e)
        {
            if (e is ArgumentException)
            {
                return Message(StatusCodes.Status503ServiceUnavailable, "The Cinnabarr settings in Jellyfin are not valid: " + e.Message);
            }

            if (e is TaskCanceledException)
            {
                return Message(StatusCodes.Status504GatewayTimeout, "Cinnabarr did not answer in time.");
            }

            _logger.LogInformation(e, "Could not reach Cinnabarr");
            return Message(StatusCodes.Status502BadGateway, "Could not reach Cinnabarr.");
        }

        // Cinnabarr's 401/403 means the saved API key is wrong. Passing it on would make the Jellyfin app think its own
        // session ended and sign the user out, so it is reported as a gateway error instead.
        private static bool IsKeyRefused(HttpResponseMessage response)
        {
            return response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden;
        }

        private IActionResult KeyRefused()
        {
            _logger.LogWarning("Cinnabarr refused the API key saved in the Cinnabarr plugin settings");
            return Message(StatusCodes.Status502BadGateway, "Cinnabarr refused the API key saved in Jellyfin. An administrator can fix it under Dashboard → Plugins → Cinnabarr.");
        }

        private IActionResult Bad(string message) => Message(StatusCodes.Status400BadRequest, message);

        private ObjectResult Message(int status, string message)
        {
            return new ObjectResult(new { message }) { StatusCode = status };
        }

        private void LogRequest(string mediaType, int externalId, string responseText)
        {
            string? title = null;
            var alreadyExisted = false;

            try
            {
                using var json = JsonDocument.Parse(responseText);

                if (json.RootElement.TryGetProperty("title", out var t))
                {
                    title = t.GetString();
                }

                if (json.RootElement.TryGetProperty("alreadyExisted", out var a) && a.ValueKind == JsonValueKind.True)
                {
                    alreadyExisted = true;
                }
            }
            catch (JsonException)
            {
            }

            _requestLog.Add(new RequestLogEntry
            {
                Time = DateTime.UtcNow,
                UserId = RoleResolver.GetUserId(User),
                UserName = User.Identity?.Name,
                MediaType = mediaType,
                ExternalId = externalId,
                Title = title,
                AlreadyExisted = alreadyExisted
            });
        }
    }
}
