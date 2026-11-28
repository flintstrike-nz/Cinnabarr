using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Cinnabarr.Requests
{
    public sealed class RequestLogEntry
    {
        public DateTime Time { get; set; }
        public Guid UserId { get; set; }
        public string? UserName { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public int ExternalId { get; set; }
        public string? Title { get; set; }
        public bool AlreadyExisted { get; set; }
    }

    public interface IRequestLog
    {
        void Add(RequestLogEntry entry);

        // Newest first. Only that user's requests when userId is given.
        IReadOnlyList<RequestLogEntry> Recent(Guid? userId, int count);
    }

    /// <summary>
    /// Who requested what, kept by the plugin because Cinnabarr has no users. One JSON line per request in the
    /// plugin's data folder; trimmed to the newest <see cref="MaxEntries"/> when it grows past twice that.
    /// </summary>
    public class RequestLog : IRequestLog
    {
        public const int MaxEntries = 2000;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions();

        private readonly object _lock = new object();
        private readonly Func<string?> _folder;
        private readonly ILogger<RequestLog> _logger;

        public RequestLog(ILogger<RequestLog> logger)
            : this(() => Plugin.Instance?.DataFolderPath, logger)
        {
        }

        internal RequestLog(Func<string?> folder, ILogger<RequestLog> logger)
        {
            _folder = folder;
            _logger = logger;
        }

        public void Add(RequestLogEntry entry)
        {
            var file = FilePath();

            if (file == null)
            {
                return;
            }

            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.AppendAllText(file, JsonSerializer.Serialize(entry, JsonOptions) + "\n");

                    var lines = File.ReadAllLines(file);

                    if (lines.Length > MaxEntries * 2)
                    {
                        File.WriteAllLines(file, lines.Skip(lines.Length - MaxEntries));
                    }
                }
                catch (IOException e)
                {
                    _logger.LogWarning(e, "Could not write the Cinnabarr request log");
                }
            }
        }

        public IReadOnlyList<RequestLogEntry> Recent(Guid? userId, int count)
        {
            var file = FilePath();

            if (file == null || !File.Exists(file))
            {
                return Array.Empty<RequestLogEntry>();
            }

            string[] lines;

            lock (_lock)
            {
                lines = File.ReadAllLines(file);
            }

            var result = new List<RequestLogEntry>();

            for (var i = lines.Length - 1; i >= 0 && result.Count < count; i--)
            {
                RequestLogEntry? entry;

                try
                {
                    entry = JsonSerializer.Deserialize<RequestLogEntry>(lines[i], JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (entry != null && (userId == null || entry.UserId == userId))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        private string? FilePath()
        {
            var folder = _folder();

            return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "requests.jsonl");
        }
    }
}
