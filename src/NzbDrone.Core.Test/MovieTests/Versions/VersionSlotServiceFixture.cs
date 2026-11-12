using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.Versions
{
    [TestFixture]
    public class VersionSlotServiceFixture : CoreTest<VersionSlotService>
    {
        private List<VersionSlot> _existing;

        [SetUp]
        public void Setup()
        {
            _existing = new List<VersionSlot>();

            Mocker.GetMock<IVersionSlotRepository>().Setup(r => r.GetByMovie(7)).Returns(() => _existing);
            Mocker.GetMock<IVersionSlotRepository>().Setup(r => r.Insert(It.IsAny<VersionSlot>())).Returns<VersionSlot>(s => s);
            Mocker.GetMock<IVersionSlotRepository>().Setup(r => r.Update(It.IsAny<VersionSlot>())).Returns<VersionSlot>(s => s);
        }

        private static VersionSlot Slot(string label) => new VersionSlot { MovieId = 7, Label = label, QualityProfileId = 5, Monitored = true };

        [Test]
        public void should_add_a_slot_with_a_trimmed_label_and_the_time_it_was_added()
        {
            var slot = Subject.Add(Slot("  4K "));

            slot.Label.Should().Be("4K");
            slot.Added.Should().BeCloseTo(System.DateTime.UtcNow, System.TimeSpan.FromMinutes(1));
            Mocker.GetMock<IVersionSlotRepository>().Verify(r => r.Insert(slot), Times.Once());
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void should_refuse_a_slot_without_a_label(string label)
        {
            Assert.Throws<NzbDroneClientException>(() => Subject.Add(Slot(label)));
        }

        [TestCase("4K/HD")]
        [TestCase("a:b")]
        [TestCase("what?")]
        public void should_refuse_a_label_that_cannot_be_part_of_a_file_name(string label)
        {
            Assert.Throws<NzbDroneClientException>(() => Subject.Add(Slot(label)));
        }

        [Test]
        public void should_refuse_a_label_the_movie_already_has_whatever_its_case()
        {
            _existing.Add(new VersionSlot { Id = 1, MovieId = 7, Label = "4K" });

            Assert.Throws<NzbDroneClientException>(() => Subject.Add(Slot("4k")));
        }

        [Test]
        public void should_refuse_more_slots_than_the_limit()
        {
            // The default slot plus seven extra ones is the limit.
            _existing.AddRange(Enumerable.Range(1, VersionSlotService.MaxSlotsPerMovie - 1).Select(i => new VersionSlot { Id = i, MovieId = 7, Label = "V" + i }));

            Assert.Throws<NzbDroneClientException>(() => Subject.Add(Slot("One more")));
        }

        [Test]
        public void should_keep_the_movie_and_added_time_when_a_slot_is_updated()
        {
            var added = new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
            var stored = new VersionSlot { Id = 3, MovieId = 7, Label = "HD", Added = added };
            _existing.Add(stored);
            Mocker.GetMock<IVersionSlotRepository>().Setup(r => r.Get(3)).Returns(stored);

            var updated = Subject.Update(new VersionSlot { Id = 3, MovieId = 99, Label = "HD", QualityProfileId = 4 });

            updated.MovieId.Should().Be(7);
            updated.Added.Should().Be(added);
            updated.QualityProfileId.Should().Be(4);
        }

        [Test]
        public void should_allow_a_slot_to_keep_its_own_label_when_updated()
        {
            var stored = new VersionSlot { Id = 3, MovieId = 7, Label = "HD" };
            _existing.Add(stored);
            Mocker.GetMock<IVersionSlotRepository>().Setup(r => r.Get(3)).Returns(stored);

            Assert.DoesNotThrow(() => Subject.Update(new VersionSlot { Id = 3, Label = "HD", QualityProfileId = 4 }));
        }

        [Test]
        public void should_refuse_to_delete_a_slot_that_still_has_a_file()
        {
            Mocker.GetMock<IVersionSlotRepository>().Setup(r => r.Get(3)).Returns(new VersionSlot { Id = 3, MovieId = 7, Label = "HD" });
            Mocker.GetMock<IMovieFileService>().Setup(s => s.GetFilesByMovie(7)).Returns(new List<MovieFile> { new MovieFile { Id = 1, VersionSlotId = 3 } });

            Assert.Throws<NzbDroneClientException>(() => Subject.Delete(3));

            Mocker.GetMock<IVersionSlotRepository>().Verify(r => r.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_delete_a_slot_without_a_file()
        {
            Mocker.GetMock<IVersionSlotRepository>().Setup(r => r.Get(3)).Returns(new VersionSlot { Id = 3, MovieId = 7, Label = "HD" });
            Mocker.GetMock<IMovieFileService>().Setup(s => s.GetFilesByMovie(7)).Returns(new List<MovieFile> { new MovieFile { Id = 1, VersionSlotId = 0 } });

            Subject.Delete(3);

            Mocker.GetMock<IVersionSlotRepository>().Verify(r => r.Delete(3), Times.Once());
        }

        [Test]
        public void should_delete_the_slots_of_deleted_movies()
        {
            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { new Movie { Id = 7 }, new Movie { Id = 8 } }, false, false));

            Mocker.GetMock<IVersionSlotRepository>().Verify(r => r.DeleteForMovies(It.Is<List<int>>(ids => ids.SequenceEqual(new[] { 7, 8 }))), Times.Once());
        }
    }
}
