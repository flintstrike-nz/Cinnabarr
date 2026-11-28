using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Cinnabarr.Web
{
    /// <summary>
    /// Adds the Cinnabarr screens to the Jellyfin web app (and so to the LG webOS app, which loads it from the server)
    /// by putting one script tag into jellyfin-web's index.html as it is served. Nothing on disk is changed, so a
    /// read-only or Docker install works, and removing the plugin removes the screens.
    /// </summary>
    public class IndexInjectionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.UseMiddleware<IndexInjectionMiddleware>();
                next(app);
            };
        }
    }

    public class IndexInjectionMiddleware
    {
        private const string Marker = "data-cinnabarr";

        private readonly RequestDelegate _next;
        private readonly ILogger<IndexInjectionMiddleware> _logger;

        public IndexInjectionMiddleware(RequestDelegate next, ILogger<IndexInjectionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!IsIndexRequest(context.Request.Method, context.Request.Path.Value))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            // Ask for the plain page every time: not compressed (so it can be edited) and not "not modified" (so a
            // copy cached before the plugin was installed is replaced).
            context.Request.Headers.Remove("Accept-Encoding");
            context.Request.Headers.Remove("If-None-Match");
            context.Request.Headers.Remove("If-Modified-Since");

            var original = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;

            try
            {
                await _next(context).ConfigureAwait(false);
            }
            finally
            {
                context.Response.Body = original;
            }

            buffer.Position = 0;

            var isHtml = context.Response.StatusCode == StatusCodes.Status200OK &&
                (context.Response.ContentType ?? string.Empty).StartsWith("text/html", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrEmpty(context.Response.Headers.ContentEncoding);

            if (!isHtml)
            {
                await buffer.CopyToAsync(original, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            var html = Encoding.UTF8.GetString(buffer.ToArray());
            var injected = Inject(html, Plugin.Instance?.Version?.ToString() ?? "0");

            if (ReferenceEquals(injected, html))
            {
                _logger.LogDebug("Cinnabarr: no </body> in index.html, nothing added");
            }

            var bytes = Encoding.UTF8.GetBytes(injected);
            context.Response.ContentLength = bytes.Length;
            context.Response.Headers.Remove("ETag");
            context.Response.Headers.Remove("Last-Modified");
            context.Response.Headers.CacheControl = "no-cache";

            await original.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
        }

        // Whether this is a request for jellyfin-web's index.html (under any base URL).
        public static bool IsIndexRequest(string method, string? path)
        {
            if (!HttpMethods.IsGet(method) || string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase);
        }

        // The page with the Cinnabarr script added before </body>, or the same string when it has no </body> or
        // already has the script.
        // The URLs are relative to /web/, so they work under any base URL without echoing the request path into the page.
        public static string Inject(string html, string version)
        {
            if (html.Contains(Marker, StringComparison.Ordinal))
            {
                return html;
            }

            var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);

            if (end < 0)
            {
                return html;
            }

            var v = Uri.EscapeDataString(version);
            var tags =
                $"<link rel=\"stylesheet\" href=\"../Cinnabarr/Web/client.css?v={v}\" {Marker}>" +
                $"<script defer src=\"../Cinnabarr/Web/client.js?v={v}\" {Marker}></script>";

            return html.Substring(0, end) + tags + html.Substring(end);
        }
    }
}
