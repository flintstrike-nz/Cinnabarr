using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Cinnabarr.Api
{
    /// <summary>
    /// The script and stylesheet added to jellyfin-web, and the game player page. They hold no data and no secrets, so
    /// they are served without sign-in (the login page loads them too); everything they fetch needs the user's
    /// Jellyfin session.
    /// </summary>
    [ApiController]
    [Route("Cinnabarr/Web")]
    [AllowAnonymous]
    public class WebAssetsController : ControllerBase
    {
        private static readonly Dictionary<string, string> Assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["client.js"] = "application/javascript; charset=utf-8",
            ["client.css"] = "text/css; charset=utf-8",
            ["play.html"] = "text/html; charset=utf-8"
        };

        [HttpGet("{file}")]
        public IActionResult Get([FromRoute] string file)
        {
            if (!Assets.TryGetValue(file ?? string.Empty, out var contentType))
            {
                return NotFound();
            }

            var assembly = typeof(Plugin).Assembly;
            var stream = assembly.GetManifestResourceStream(typeof(Plugin).Namespace + ".Web." + file!.ToLowerInvariant());

            if (stream == null)
            {
                return NotFound();
            }

            // The URL carries the plugin version, so a new version is fetched fresh.
            Response.Headers.CacheControl = "public, max-age=86400";

            return File(stream, contentType);
        }
    }
}
