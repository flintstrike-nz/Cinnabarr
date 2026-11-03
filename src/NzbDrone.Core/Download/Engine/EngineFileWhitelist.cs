using System;
using System.Collections.Generic;
using System.IO;

namespace NzbDrone.Core.Download.Engine
{
    // Only video and subtitle files are ever downloaded (roadmap §4.8). Only the real final extension counts,
    // so "movie.mkv.exe" is not a video.
    public static class EngineFileWhitelist
    {
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".mp4", ".m4v", ".avi", ".ts", ".m2ts", ".webm"
        };

        private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".sup"
        };

        public static bool IsVideo(string fileName)
        {
            return VideoExtensions.Contains(GetExtension(fileName));
        }

        public static bool IsSubtitle(string fileName)
        {
            return SubtitleExtensions.Contains(GetExtension(fileName));
        }

        public static bool IsWanted(string fileName)
        {
            return IsVideo(fileName) || IsSubtitle(fileName);
        }

        private static string GetExtension(string fileName)
        {
            return fileName == null ? string.Empty : Path.GetExtension(fileName.Replace('\\', '/'));
        }
    }
}
