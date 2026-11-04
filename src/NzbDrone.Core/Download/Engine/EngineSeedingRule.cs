using System;
using System.Collections.Generic;
using System.Linq;

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

    // One seeding goal (roadmap §4.10), with where it came from (e.g. "global", "tracker").
    public class EngineSeedingRule
    {
        public EngineSeedingMode Mode { get; set; }
        public int Minutes { get; set; }
        public double Ratio { get; set; }
        public string Source { get; set; }

        public bool UsesTime => Mode is EngineSeedingMode.Time or EngineSeedingMode.Either or EngineSeedingMode.Both;
        public bool UsesRatio => Mode is EngineSeedingMode.Ratio or EngineSeedingMode.Either or EngineSeedingMode.Both;

        // Per-file time runs from when that file was imported; per-file ratio is the torrent's (libtorrent counts
        // upload per torrent, not per file).
        public bool IsMet(TimeSpan seeded, double ratio)
        {
            var timeMet = seeded.TotalMinutes >= Minutes;
            var ratioMet = ratio >= Ratio;

            return Mode switch
            {
                EngineSeedingMode.None => true,
                EngineSeedingMode.Time => timeMet,
                EngineSeedingMode.Ratio => ratioMet,
                EngineSeedingMode.Either => timeMet || ratioMet,
                EngineSeedingMode.Both => timeMet && ratioMet,
                _ => true
            };
        }

        // Every rule must be met (the global default and the tracker's minimum are both goals).
        public static bool AreMet(IEnumerable<EngineSeedingRule> rules, TimeSpan seeded, double ratio)
        {
            return rules.All(r => r.IsMet(seeded, ratio));
        }
    }
}
