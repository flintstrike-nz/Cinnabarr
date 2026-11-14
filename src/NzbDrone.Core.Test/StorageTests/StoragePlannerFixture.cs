using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Storage;

namespace NzbDrone.Core.Test.StorageTests
{
    [TestFixture]
    public class StoragePlannerFixture
    {
        private const long Gb = 1024L * 1024 * 1024;

        private static StorageDrive Drive(string path, long totalGb, long freeGb) =>
            new StorageDrive { Path = path, TotalSpace = totalGb * Gb, FreeSpace = freeGb * Gb };

        private static StorageItem Item(int id, string drive, long sizeGb, long incomingGb = 0, bool busy = false) =>
            new StorageItem { MediaType = StorageMediaType.Series, Id = id, Title = "S" + id, Path = drive + "/S" + id, DrivePath = drive, Size = sizeGb * Gb, IncomingBytes = incomingGb * Gb, IsBusy = busy };

        private static StoragePlanSettings Settings() => new StoragePlanSettings { TolerancePercent = 10, ReservePercent = 5, MaxBytesPerRun = 1000 * Gb };

        [Test]
        public void should_do_nothing_with_one_drive()
        {
            StoragePlanner.Plan(new[] { Drive("/a", 1000, 100) }, new[] { Item(1, "/a", 50) }, Settings()).Should().BeEmpty();
        }

        [Test]
        public void should_do_nothing_when_drives_are_within_tolerance()
        {
            var drives = new[] { Drive("/a", 1000, 400), Drive("/b", 1000, 450) };

            StoragePlanner.Plan(drives, new[] { Item(1, "/a", 100) }, Settings()).Should().BeEmpty();
        }

        [Test]
        public void should_move_from_the_fullest_to_the_emptiest_drive()
        {
            var drives = new[] { Drive("/a", 1000, 100), Drive("/b", 1000, 800) };
            var items = new[] { Item(1, "/a", 100), Item(2, "/a", 300), Item(3, "/a", 200) };

            var moves = StoragePlanner.Plan(drives, items, Settings());

            moves.Should().NotBeEmpty();
            moves.All(m => m.FromDrive == "/a" && m.ToDrive == "/b").Should().BeTrue();

            // Free space ends up closer together than the 70 points it started at.
            var moved = moves.Sum(m => m.Item.Size) / Gb;
            var gap = ((100 + moved) - (800 - moved)) / 10.0;
            System.Math.Abs(gap).Should().BeLessThan(30);
        }

        [Test]
        public void should_not_move_an_item_that_is_being_downloaded()
        {
            var drives = new[] { Drive("/a", 1000, 100), Drive("/b", 1000, 800) };

            StoragePlanner.Plan(drives, new[] { Item(1, "/a", 300, busy: true) }, Settings()).Should().BeEmpty();
        }

        [Test]
        public void should_count_incoming_downloads_as_used_space()
        {
            // Equal free space now, but /a has 300 GB on the way.
            var drives = new[] { Drive("/a", 1000, 500), Drive("/b", 1000, 500) };
            var items = new[] { Item(1, "/a", 100, incomingGb: 300), Item(2, "/a", 50) };

            var moves = StoragePlanner.Plan(drives, items, Settings());

            // Without the incoming data the drives are level and nothing would move.
            StoragePlanner.Plan(drives, items.Select(i => Item(i.Id, "/a", i.Size / Gb)).ToList(), Settings()).Should().BeEmpty();
            moves.Should().NotBeEmpty();
            moves.All(m => m.FromDrive == "/a" && m.ToDrive == "/b").Should().BeTrue();
        }

        [Test]
        public void should_not_fill_the_destination_past_the_reserve()
        {
            var drives = new[] { Drive("/a", 1000, 20), Drive("/b", 1000, 100) };

            // Moving 60 GB would leave /b with 4% free, under the 5% reserve.
            StoragePlanner.Plan(drives, new[] { Item(1, "/a", 60) }, Settings()).Should().BeEmpty();
        }

        [Test]
        public void should_stop_at_the_per_run_limit()
        {
            var drives = new[] { Drive("/a", 1000, 50), Drive("/b", 1000, 900) };
            var items = Enumerable.Range(1, 8).Select(i => Item(i, "/a", 50)).ToList();
            var settings = Settings();
            settings.MaxBytesPerRun = 100 * Gb;

            StoragePlanner.Plan(drives, items, settings).Sum(m => m.Item.Size).Should().BeLessOrEqualTo(100 * Gb);
        }
    }
}
