using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Storage
{
    public class StorageDrive
    {
        public string Path { get; set; }
        public long TotalSpace { get; set; }
        public long FreeSpace { get; set; }
    }

    public class StorageItem
    {
        public StorageMediaType MediaType { get; set; }
        public int Id { get; set; }
        public string Title { get; set; }
        public string Path { get; set; }

        // The drive (root folder path) the item is on.
        public string DrivePath { get; set; }

        // What it takes on disk now.
        public long Size { get; set; }

        // What is on its way: downloads in flight and what the calendar says is coming. It moves with the item.
        public long IncomingBytes { get; set; }

        // Not moved while it is being downloaded to.
        public bool IsBusy { get; set; }
    }

    public enum StorageMediaType
    {
        Series = 1,
        Movie = 2
    }

    public class StorageMove
    {
        public StorageItem Item { get; set; }
        public string FromDrive { get; set; }
        public string ToDrive { get; set; }
    }

    public class StoragePlanSettings
    {
        // Drives are balanced once the gap in free space between the fullest and emptiest is bigger than this (percentage points).
        public double TolerancePercent { get; set; } = 10;

        // A drive is never filled past this much free space (percent of its size).
        public double ReservePercent { get; set; } = 5;

        // The most that is moved in one run.
        public long MaxBytesPerRun { get; set; } = 200L * 1024 * 1024 * 1024;
    }

    /// <summary>
    /// Plans moves between the drives of one library so free space ends up even. What is coming (downloads and the
    /// calendar) counts as space already used, so a drive that is about to fill is emptied before it does.
    /// </summary>
    public static class StoragePlanner
    {
        public static List<StorageMove> Plan(IReadOnlyList<StorageDrive> drives, IEnumerable<StorageItem> items, StoragePlanSettings settings)
        {
            var moves = new List<StorageMove>();

            if (drives.Count < 2)
            {
                return moves;
            }

            var free = drives.ToDictionary(d => d.Path, d => d.FreeSpace - Incoming(items, d.Path), StringComparer.OrdinalIgnoreCase);
            var total = drives.ToDictionary(d => d.Path, d => d.TotalSpace, StringComparer.OrdinalIgnoreCase);
            var location = items.ToDictionary(i => (i.MediaType, i.Id), i => i.DrivePath);
            var budget = settings.MaxBytesPerRun;

            for (var round = 0; round < 500; round++)
            {
                var ordered = drives.Where(d => d.TotalSpace > 0).OrderBy(d => Pct(free[d.Path], total[d.Path])).ToList();
                var from = ordered.First();
                var to = ordered.Last();
                var gap = Pct(free[to.Path], total[to.Path]) - Pct(free[from.Path], total[from.Path]);

                if (gap <= settings.TolerancePercent)
                {
                    break;
                }

                var candidate = items
                    .Where(i => !i.IsBusy && string.Equals(location[(i.MediaType, i.Id)], from.Path, StringComparison.OrdinalIgnoreCase))
                    .Select(i => new { Item = i, Weight = i.Size + i.IncomingBytes })
                    .Where(c => c.Weight > 0 && c.Item.Size <= budget && Fits(c.Weight, free[to.Path], total[to.Path], settings))
                    .Select(c => new
                    {
                        c.Item,
                        c.Weight,
                        NewGap = Math.Abs(Pct(free[to.Path] - c.Weight, total[to.Path]) - Pct(free[from.Path] + c.Weight, total[from.Path]))
                    })
                    .Where(c => c.NewGap < gap)
                    .OrderBy(c => c.NewGap)
                    .FirstOrDefault();

                if (candidate == null)
                {
                    break;
                }

                free[from.Path] += candidate.Weight;
                free[to.Path] -= candidate.Weight;
                budget -= candidate.Item.Size;
                location[(candidate.Item.MediaType, candidate.Item.Id)] = to.Path;
                moves.Add(new StorageMove { Item = candidate.Item, FromDrive = from.Path, ToDrive = to.Path });
            }

            return moves;
        }

        private static long Incoming(IEnumerable<StorageItem> items, string drive)
        {
            return items.Where(i => string.Equals(i.DrivePath, drive, StringComparison.OrdinalIgnoreCase)).Sum(i => i.IncomingBytes);
        }

        private static double Pct(long free, long total) => total <= 0 ? 0 : free * 100.0 / total;

        private static bool Fits(long weight, long free, long total, StoragePlanSettings settings)
        {
            return Pct(free - weight, total) >= settings.ReservePercent;
        }
    }
}
