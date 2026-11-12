using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine.Movies;
using NzbDrone.Core.DecisionEngine.Movies.Specifications;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.Versions
{
    // A version slot is read as a view of the movie that carries the slot's profile, monitored state and file, so the
    // decision engine, the importer and the name builder work on it unchanged.
    [TestFixture]
    public class VersionViewFixture : CoreTest<VersionSlotService>
    {
        private Movie _movie;
        private VersionSlot _slot;
        private QualityProfile _profile;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie { Id = 7, Monitored = true, QualityProfileId = 1, MovieFileId = 3, MovieFile = new MovieFile { Id = 3 }, MovieMetadata = new MovieMetadata { Title = "Alien", Year = 1979 } };
            _slot = new VersionSlot { Id = 5, MovieId = 7, Label = "4K", QualityProfileId = 9, Monitored = true };
            _profile = new QualityProfile { Id = 9, Name = "Ultra-HD" };

            Mocker.GetMock<IVersionSlotRepository>().Setup(r => r.Get(5)).Returns(_slot);
            Mocker.GetMock<IQualityProfileService>().Setup(s => s.Get(9)).Returns(_profile);
            Mocker.GetMock<IMovieFileService>().Setup(s => s.GetFilesByMovie(7)).Returns(new List<MovieFile> { new MovieFile { Id = 3, VersionSlotId = 0 }, new MovieFile { Id = 4, VersionSlotId = 5 } });
        }

        [Test]
        public void the_default_slot_is_the_movie_itself()
        {
            Subject.GetView(_movie, 0).Should().BeSameAs(_movie);
        }

        [Test]
        public void should_carry_the_slots_profile_file_and_label()
        {
            var view = Subject.GetView(_movie, 5);

            view.Id.Should().Be(7);
            view.VersionSlotId.Should().Be(5);
            view.VersionLabel.Should().Be("4K");
            view.QualityProfileId.Should().Be(9);
            view.QualityProfile.Should().BeSameAs(_profile);
            view.MovieFileId.Should().Be(4);
            view.MovieFile.Id.Should().Be(4);
            view.Title.Should().Be("Alien");
        }

        [Test]
        public void should_leave_the_movie_itself_as_it_was()
        {
            Subject.GetView(_movie, 5);

            _movie.QualityProfileId.Should().Be(1);
            _movie.MovieFileId.Should().Be(3);
            _movie.VersionSlotId.Should().Be(0);
            _movie.VersionLabel.Should().BeNull();
        }

        [Test]
        public void should_have_no_file_when_the_slot_has_none()
        {
            Mocker.GetMock<IMovieFileService>().Setup(s => s.GetFilesByMovie(7)).Returns(new List<MovieFile> { new MovieFile { Id = 3 } });

            var view = Subject.GetView(_movie, 5);

            view.MovieFileId.Should().Be(0);
            view.MovieFile.Should().BeNull();
            view.HasFile.Should().BeFalse();
        }

        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, true)]
        public void should_be_monitored_only_when_the_movie_and_the_slot_both_are(bool movieMonitored, bool slotMonitored, bool expected)
        {
            _movie.Monitored = movieMonitored;
            _slot.Monitored = slotMonitored;

            Subject.GetView(_movie, 5).Monitored.Should().Be(expected);
        }

        [Test]
        public void should_refuse_a_slot_of_another_movie()
        {
            _slot.MovieId = 99;

            Assert.Throws<NzbDroneClientException>(() => Subject.GetView(_movie, 5));
        }

        [Test]
        public void should_name_a_file_of_a_version_with_its_label_when_the_format_has_no_version_token()
        {
            var builder = Mocker.Resolve<FileNameBuilder>();
            var config = NamingPresets.Get(NamingPreset.Jellyfin);
            config.StandardMovieFormat = "{Movie Title} ({Release Year})";
            Mocker.GetMock<INamingConfigService>().Setup(c => c.GetConfig()).Returns(config);
            Mocker.GetMock<IQualityDefinitionService>().Setup(s => s.Get(It.IsAny<Quality>())).Returns<Quality>(q => new QualityDefinition(q) { Title = q.Name });

            var file = new MovieFile { Quality = new QualityModel(Quality.Bluray2160p), RelativePath = "x.mkv" };

            builder.BuildFileName(_movie, file).Should().Be("Alien (1979)");
            builder.BuildFileName(Subject.GetView(_movie, 5), file).Should().Be("Alien (1979) - 4K");
        }

        [Test]
        public void should_place_the_label_where_a_version_token_says()
        {
            var builder = Mocker.Resolve<FileNameBuilder>();
            var config = NamingPresets.Get(NamingPreset.Jellyfin);
            Mocker.GetMock<INamingConfigService>().Setup(c => c.GetConfig()).Returns(config);
            Mocker.GetMock<IQualityDefinitionService>().Setup(s => s.Get(It.IsAny<Quality>())).Returns<Quality>(q => new QualityDefinition(q) { Title = q.Name });

            var file = new MovieFile { Quality = new QualityModel(Quality.Bluray2160p), RelativePath = "x.mkv" };

            // The Jellyfin preset ends with { - Version}: nothing for the movie itself, " - 4K" for the version.
            builder.BuildFileName(_movie, file).Should().Be("Alien (1979)");
            builder.BuildFileName(Subject.GetView(_movie, 5), file).Should().Be("Alien (1979) - 4K");

            config.StandardMovieFormat = "{Movie Title} ({Release Year}) [{Version}]";
            builder.BuildFileName(Subject.GetView(_movie, 5), file).Should().Be("Alien (1979) [4K]");
        }
    }

    [TestFixture]
    public class VersionQueueSpecificationFixture : CoreTest<MovieQueueSpecification>
    {
        private static Movie View(int movieId, int slotId)
        {
            return new Movie { Id = movieId, Slot = slotId == 0 ? null : new VersionSlot { Id = slotId, Label = "V" + slotId }, QualityProfile = Profile() };
        }

        // A profile whose cutoff the queued 720p release already meets.
        private static QualityProfile Profile()
        {
            return new QualityProfile
            {
                UpgradeAllowed = true,
                Cutoff = Quality.HDTV720p.Id,
                Items = NzbDrone.Core.Test.Qualities.QualityFixture.GetDefaultQualities(Quality.SDTV, Quality.HDTV720p, Quality.Bluray2160p),
                FormatItems = new List<NzbDrone.Core.Profiles.ProfileFormatItem>()
            };
        }

        private RemoteMovie Remote(Movie movie)
        {
            return new RemoteMovie { Movie = movie, ParsedMovieInfo = new ParsedMovieInfo { Quality = new QualityModel(Quality.HDTV720p) }, Release = new ReleaseInfo() };
        }

        private void GivenQueued(Movie movie)
        {
            Mocker.GetMock<NzbDrone.Core.CustomFormats.ICustomFormatCalculationService>()
                  .Setup(s => s.ParseCustomFormat(It.IsAny<RemoteMovie>(), It.IsAny<long>()))
                  .Returns(new List<NzbDrone.Core.CustomFormats.CustomFormat>());

            Mocker.GetMock<NzbDrone.Core.Queue.IQueueService>().Setup(q => q.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue>
            {
                new NzbDrone.Core.Queue.Queue { Id = 1, RemoteMovie = Remote(movie), Size = 100, TrackedDownloadState = TrackedDownloadState.Downloading }
            });
        }

        [Test]
        public void a_download_for_one_version_does_not_block_another_version_of_the_same_movie()
        {
            GivenQueued(View(7, 5));

            Subject.IsSatisfiedBy(Remote(View(7, 0)), new MovieReleaseDecisionInformation(false, null)).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(Remote(View(7, 6)), new MovieReleaseDecisionInformation(false, null)).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_download_for_a_version_blocks_a_release_for_the_same_version()
        {
            GivenQueued(View(7, 5));

            // The mocked upgrade checks say the cutoff is met, so the queued release counts.
            Subject.IsSatisfiedBy(Remote(View(7, 5)), new MovieReleaseDecisionInformation(false, null)).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class VersionHistoryFixture : CoreTest<MovieHistoryService>
    {
        [Test]
        public void should_record_the_version_a_release_was_grabbed_for()
        {
            var inserted = new List<MovieHistory>();
            Mocker.GetMock<IMovieHistoryRepository>().Setup(r => r.Insert(It.IsAny<MovieHistory>())).Callback<MovieHistory>(inserted.Add);

            var movie = new Movie { Id = 7, Slot = new VersionSlot { Id = 5, Label = "4K" } };
            var remote = new RemoteMovie
            {
                Movie = movie,
                Release = new ReleaseInfo { Title = "Alien.2160p", Guid = "g" },
                ParsedMovieInfo = new ParsedMovieInfo { Quality = new QualityModel(Quality.Bluray2160p) },
                Languages = new List<NzbDrone.Core.Languages.Language>()
            };

            Subject.Handle(new NzbDrone.Core.Download.MovieGrabbedEvent(remote) { DownloadId = "ABC" });

            inserted.Single().VersionSlotId.Should().Be(5);
            inserted.Single().Data[MovieHistory.VERSION_SLOT_ID].Should().Be("5");
        }

        [Test]
        public void should_not_record_a_version_for_the_default_slot()
        {
            var inserted = new List<MovieHistory>();
            Mocker.GetMock<IMovieHistoryRepository>().Setup(r => r.Insert(It.IsAny<MovieHistory>())).Callback<MovieHistory>(inserted.Add);

            var remote = new RemoteMovie
            {
                Movie = new Movie { Id = 7 },
                Release = new ReleaseInfo { Title = "Alien.1080p", Guid = "g" },
                ParsedMovieInfo = new ParsedMovieInfo { Quality = new QualityModel(Quality.Bluray1080p) },
                Languages = new List<NzbDrone.Core.Languages.Language>()
            };

            Subject.Handle(new NzbDrone.Core.Download.MovieGrabbedEvent(remote) { DownloadId = "ABC" });

            inserted.Single().VersionSlotId.Should().Be(0);
            inserted.Single().Data.Should().NotContainKey(MovieHistory.VERSION_SLOT_ID);
        }
    }

    [TestFixture]
    public class VersionFileAddedFixture : CoreTest<MovieService>
    {
        [Test]
        public void a_file_of_another_version_is_not_the_movies_own_file()
        {
            var movie = new Movie { Id = 7, MovieFileId = 3 };

            Subject.Handle(new MovieFileAddedEvent(new MovieFile { Id = 4, MovieId = 7, VersionSlotId = 5, Movie = movie }));

            movie.MovieFileId.Should().Be(3);
            Mocker.GetMock<IMovieRepository>().Verify(r => r.Update(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public void a_default_file_becomes_the_movies_file()
        {
            var movie = new Movie { Id = 7, MovieFileId = 0 };

            Subject.Handle(new MovieFileAddedEvent(new MovieFile { Id = 4, MovieId = 7, VersionSlotId = 0, Movie = movie }));

            movie.MovieFileId.Should().Be(4);
            Mocker.GetMock<IMovieRepository>().Verify(r => r.Update(movie), Times.Once());
        }
    }
}
