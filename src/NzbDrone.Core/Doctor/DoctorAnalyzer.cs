using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Doctor
{
    /// <summary>
    /// Finds files that are worse copies of another file for the same episode or movie. Copies at different
    /// resolutions are deliberate versions (4K next to 1080p) and are left alone: only copies at the same resolution
    /// compete. The better copy is the one the user protected, else the one that wins on source, revision, video
    /// bitrate and size, in that order; a difference under 10% does not count for bitrate or size.
    /// </summary>
    public static class DoctorAnalyzer
    {
        private const double Tolerance = 0.10;

        public static List<DoctorDuplicate> FindDuplicates(IEnumerable<DoctorCandidate> candidates)
        {
            var result = new List<DoctorDuplicate>();

            var groups = candidates.GroupBy(c => new { c.MediaType, c.ItemId, c.GroupKey, Resolution = ResolutionOf(c) });

            foreach (var group in groups)
            {
                var files = group.ToList();

                if (files.Count < 2)
                {
                    continue;
                }

                var best = files.Where(f => f.IsProtected).OrderBy(f => f.DateAdded).FirstOrDefault();

                if (best == null)
                {
                    best = files[0];

                    foreach (var file in files.Skip(1))
                    {
                        if (Compare(file, best, out _) > 0)
                        {
                            best = file;
                        }
                    }
                }

                foreach (var file in files.Where(f => f != best && !f.IsProtected))
                {
                    Compare(best, file, out var reason);
                    result.Add(new DoctorDuplicate { Worse = file, Better = best, Reason = reason });
                }
            }

            return result;
        }

        /// <summary>Positive when a is the better copy, negative when b is, zero for a tie.</summary>
        public static int Compare(DoctorCandidate a, DoctorCandidate b, out string reason)
        {
            var sourceA = SourceRank(a);
            var sourceB = SourceRank(b);

            if (sourceA != sourceB)
            {
                reason = $"Lower source ({SourceName(sourceA > sourceB ? b : a)} instead of {SourceName(sourceA > sourceB ? a : b)})";
                return sourceA.CompareTo(sourceB);
            }

            var revisionA = RevisionOf(a);
            var revisionB = RevisionOf(b);

            if (revisionA != revisionB)
            {
                reason = "Older revision (no PROPER or REPACK)";
                return revisionA.CompareTo(revisionB);
            }

            var bitrateA = a.MediaInfo?.VideoBitrate ?? 0;
            var bitrateB = b.MediaInfo?.VideoBitrate ?? 0;

            if (bitrateA > 0 && bitrateB > 0 && Differs(bitrateA, bitrateB))
            {
                reason = $"Lower video bitrate ({FormatBitrate(Math.Min(bitrateA, bitrateB))} instead of {FormatBitrate(Math.Max(bitrateA, bitrateB))})";
                return bitrateA.CompareTo(bitrateB);
            }

            if (Differs(a.Size, b.Size))
            {
                reason = $"Smaller file ({Smaller(a, b).Size.SizeSuffix()} instead of {Larger(a, b).Size.SizeSuffix()})";
                return a.Size.CompareTo(b.Size);
            }

            reason = "Same quality; added later";

            // A tie goes to the copy that has been there longest.
            return b.DateAdded.CompareTo(a.DateAdded);
        }

        private static DoctorCandidate Smaller(DoctorCandidate a, DoctorCandidate b) => a.Size <= b.Size ? a : b;

        private static DoctorCandidate Larger(DoctorCandidate a, DoctorCandidate b) => a.Size > b.Size ? a : b;

        private static bool Differs(long x, long y)
        {
            var max = Math.Max(x, y);

            return max > 0 && Math.Abs(x - y) / (double)max > Tolerance;
        }

        private static int ResolutionOf(DoctorCandidate c)
        {
            var resolution = c.Quality?.Quality?.Resolution ?? 0;

            return resolution > 0 ? resolution : c.MediaInfo?.Height ?? 0;
        }

        private static int RevisionOf(DoctorCandidate c) => ((c.Quality?.Revision?.Real * 100) + c.Quality?.Revision?.Version) ?? 0;

        private static int SourceRank(DoctorCandidate c)
        {
            switch (c.Quality?.Quality?.Source ?? QualitySource.Unknown)
            {
                case QualitySource.BlurayRaw: return 8;
                case QualitySource.Bluray: return 7;
                case QualitySource.Web: return 6;
                case QualitySource.WebRip: return 5;
                case QualitySource.TelevisionRaw: return 4;
                case QualitySource.Television: return 3;
                case QualitySource.DVD: return 2;
                default: return 1;
            }
        }

        private static string SourceName(int rank)
        {
            return rank switch
            {
                8 => "Remux",
                7 => "Bluray",
                6 => "WEB-DL",
                5 => "WEBRip",
                4 => "Raw HDTV",
                3 => "HDTV",
                2 => "DVD",
                _ => "unknown source"
            };
        }

        private static string SourceName(DoctorCandidate c) => SourceName(SourceRank(c));

        private static string FormatBitrate(long bitsPerSecond) => $"{bitsPerSecond / 1_000_000.0:0.0} Mbps";
    }
}
