using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.Versions
{
    [TestFixture]
    public class VersionSlotRepositoryFixture : DbTest<VersionSlotRepository, VersionSlot>
    {
        private static VersionSlot Slot(int movieId, string label) =>
            new VersionSlot { MovieId = movieId, Label = label, QualityProfileId = 3, Monitored = true, Added = System.DateTime.UtcNow };

        [Test]
        public void should_find_the_slots_of_one_movie()
        {
            Db.InsertMany(new List<VersionSlot> { Slot(1, "4K"), Slot(1, "HD"), Slot(2, "4K") });

            Subject.GetByMovie(1).Should().HaveCount(2);
            Subject.GetByMovie(2).Should().ContainSingle().Which.Label.Should().Be("4K");
        }

        [Test]
        public void should_delete_the_slots_of_the_given_movies_only()
        {
            Db.InsertMany(new List<VersionSlot> { Slot(1, "4K"), Slot(2, "4K"), Slot(3, "4K") });

            Subject.DeleteForMovies(new List<int> { 1, 2 });

            Subject.All().Should().ContainSingle().Which.MovieId.Should().Be(3);
        }
    }
}
