using System;
using System.IO;
using System.Text;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.Download.Engine
{
    public interface IEngineSubtitleValidator
    {
        // Checks a downloaded subtitle before it is published (roadmap §4.8 "Subtitles are validated separately"):
        // a sensible size, and for text formats, content that reads as that format. Returns why it was rejected, or
        // null if it passes.
        string Validate(string path, string finalName);
    }

    public class EngineSubtitleValidator : IEngineSubtitleValidator
    {
        // Image subtitles (PGS, VobSub) of a long episode stay well below this.
        public const long MaxSizeBytes = 200L * 1024 * 1024;
        private const int SampleBytes = 64 * 1024;

        private readonly IDiskProvider _diskProvider;

        public EngineSubtitleValidator(IDiskProvider diskProvider)
        {
            _diskProvider = diskProvider;
        }

        public string Validate(string path, string finalName)
        {
            var size = _diskProvider.GetFileSize(path);

            if (size <= 0)
            {
                return "it is empty";
            }

            if (size > MaxSizeBytes)
            {
                return $"it is too large for a subtitle ({size} bytes)";
            }

            var extension = Path.GetExtension(finalName).ToLowerInvariant();
            var marker = extension switch
            {
                ".srt" => "-->",
                ".vtt" => "-->",
                ".ass" or ".ssa" => "[Events]",
                ".idx" => "# VobSub index file",
                _ => null
            };

            // sub and sup are binary image formats: only their size is checked.
            if (marker == null)
            {
                return null;
            }

            var text = ReadText(path);

            if (text == null)
            {
                return "it isn't a text file";
            }

            return text.Contains(marker, StringComparison.OrdinalIgnoreCase) ? null : $"it doesn't read as {extension.TrimStart('.')} (no \"{marker}\" in its first {SampleBytes / 1024} KB)";
        }

        // The start of the file as text, or null if it is binary. UTF-16 files are recognised by their byte-order
        // mark; anything else is read as UTF-8 (or a single-byte code page, which decodes the same markers).
        private string ReadText(string path)
        {
            var buffer = new byte[SampleBytes];
            int read;

            using (var stream = _diskProvider.OpenReadStream(path))
            {
                read = stream.Read(buffer, 0, buffer.Length);
            }

            if (read >= 2 && ((buffer[0] == 0xFF && buffer[1] == 0xFE) || (buffer[0] == 0xFE && buffer[1] == 0xFF)))
            {
                var encoding = buffer[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode;

                return encoding.GetString(buffer, 2, (read - 2) & ~1);
            }

            if (Array.IndexOf(buffer, (byte)0, 0, read) >= 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(buffer, 0, read);
        }
    }
}
