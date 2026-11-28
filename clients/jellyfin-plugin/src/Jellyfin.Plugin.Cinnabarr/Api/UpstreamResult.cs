using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Cinnabarr.Api
{
    /// <summary>
    /// Sends Cinnabarr's answer back to the app as it is: status, body and the few headers that matter (type, length,
    /// ranges). The body is streamed, so a ROM is never held in memory. Disposes the upstream response.
    /// </summary>
    public sealed class UpstreamResult : IActionResult
    {
        private readonly HttpResponseMessage _response;

        public UpstreamResult(HttpResponseMessage response)
        {
            _response = response;
        }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            using (_response)
            {
                var target = context.HttpContext.Response;
                target.StatusCode = (int)_response.StatusCode;

                var content = _response.Content;
                var headers = content.Headers;

                if (headers.ContentType != null)
                {
                    target.ContentType = headers.ContentType.ToString();
                }

                if (headers.ContentLength.HasValue)
                {
                    target.ContentLength = headers.ContentLength;
                }

                if (headers.ContentRange != null)
                {
                    target.Headers["Content-Range"] = headers.ContentRange.ToString();
                }

                if (_response.Headers.AcceptRanges.Count > 0)
                {
                    target.Headers["Accept-Ranges"] = string.Join(", ", _response.Headers.AcceptRanges);
                }

                if (headers.ContentDisposition != null)
                {
                    target.Headers["Content-Disposition"] = headers.ContentDisposition.ToString();
                }

                // Cinnabarr's data changes all the time; covers can be cached briefly.
                target.Headers["Cache-Control"] = headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
                    ? "private, max-age=3600"
                    : "no-store";

                if (_response.StatusCode == System.Net.HttpStatusCode.NoContent || context.HttpContext.Request.Method == HttpMethods.Head)
                {
                    return;
                }

                await using var stream = await content.ReadAsStreamAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
                await stream.CopyToAsync(target.Body, context.HttpContext.RequestAborted).ConfigureAwait(false);
            }
        }
    }
}
