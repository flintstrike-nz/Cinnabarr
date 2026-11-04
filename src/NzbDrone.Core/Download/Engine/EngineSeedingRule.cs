using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Download.Engine
{
    public enum EngineSeedingMode
    {
        // Don't seed: the torrent is removed (its files kept) once its files are in the library.
        None = 0,
        Time = 1,
        Ratio = 2,

        // Whichever of the time and the ratio comes first.
        Either = 3,

        // Both the time and the ratio.
        Both = 4
    }

    // One seeding goal (roadmap §4.10), with where it came from (e.g. "global", "tracker"). Stored on a series, a
    // season or an episode to override the global goal.
    public class EngineSeedingRule : IEmbeddedDocument
    {
        public EngineSeedingMode Mode { get; set; }
        public int Minutes { get; set; }
        public double Ratio { get; set; }
        [JsonIgnore]
        public string Source { get; set; }

        public const double RatioTolerance = 0.001;

        [JsonIgnore]
        public bool UsesTime => Mode is EngineSeedingMode.Time or EngineSeedingMode.Either or EngineSeedingMode.Both;
        [JsonIgnore]
        public bool UsesRatio => Mode is EngineSeedingMode.Ratio or EngineSeedingMode.Either or EngineSeedingMode.Both;

        // Per-file time runs from when that file was imported; per-file ratio is the torrent's (libtorrent counts
        // upload per torrent, not per file).
        public bool IsMet(TimeSpan seeded, double ratio)
        {
            var timeMet = seeded.TotalMinutes >= Minutes;

            // The engine reports ratios as single-precision floats (1.4 arrives as 1.3999999…).
            var ratioMet = ratio >= Ratio - RatioTolerance;

            return Mode switch
            {
                EngineSeedingMode.None => true,
                EngineSeedingMode.Time => timeMet,
                EngineSeedingMode.Ratio => ratioMet,
                EngineSeedingMode.Either => timeMet || ratioMet,
                EngineSeedingMode.Both => timeMet && ratioMet,

                // A mode this version doesn't know is never taken as met: seeding goes on rather than stopping early.
                _ => false
            };
        }

        public override bool Equals(object obj)
        {
            return obj is EngineSeedingRule other &&
                   other.Mode == Mode &&
                   other.Minutes == Minutes &&
                   Math.Abs(other.Ratio - Ratio) < RatioTolerance &&
                   other.Source == Source;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Mode, Minutes, Source);
        }

        // Every rule must be met (the global default and the tracker's minimum are both goals).
        public static bool AreMet(IEnumerable<EngineSeedingRule> rules, TimeSpan seeded, double ratio)
        {
            return rules.All(r => r.IsMet(seeded, ratio));
        }
    }
}
