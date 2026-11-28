using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Cinnabarr.Access;
using Jellyfin.Plugin.Cinnabarr.Client;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Cinnabarr.Api
{
    public sealed class TestConnectionRequest
    {
        public string? Url { get; set; }
        public string? ApiKey { get; set; }
    }

    public sealed class MeResponse
    {
        public string[] Roles { get; set; } = System.Array.Empty<string>();
        public bool Configured { get; set; }
    }

    /// <summary>
    /// The endpoints the Jellyfin apps call. Every one needs a signed-in Jellyfin user; what they may do comes from
    /// <see cref="RoleResolver"/>. Calls to Cinnabarr are added per feature (J3 onwards), never as a pass-through.
    /// </summary>
    [ApiController]
    [Route("Cinnabarr")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    public class CinnabarrController : ControllerBase
    {
        private readonly ICinnabarrClient _client;

        public CinnabarrController(ICinnabarrClient client)
        {
            _client = client;
        }

        // What the signed-in user may do, so the apps only show what works.
        [HttpGet("Me")]
        public ActionResult<MeResponse> Me()
        {
            var config = Plugin.Instance?.Configuration;

            return new MeResponse
            {
                Roles = RoleResolver.Names(RoleResolver.Resolve(User, config)),
                Configured = config != null && !string.IsNullOrWhiteSpace(config.CinnabarrUrl) && !string.IsNullOrWhiteSpace(config.ApiKey)
            };
        }

        // Administrators only: check a Cinnabarr URL and key before saving them (or the saved ones when both are empty).
        [HttpPost("Test")]
        [Authorize(Policy = Policies.RequiresElevation)]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<CinnabarrStatus>> Test([FromBody] TestConnectionRequest request, CancellationToken cancellationToken)
        {
            return await _client.TestAsync(request?.Url, request?.ApiKey, cancellationToken).ConfigureAwait(false);
        }
    }
}
