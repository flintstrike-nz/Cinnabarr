using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Movies;
using NzbDrone.Core.DecisionEngine.Movies.Specifications;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.CustomFormats;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests.Movies
{
    internal static class MovieSpecTestData
    {
        public static QualityProfile Profile()
        {
            CustomFormatsTestHelpers.GivenCustomFormats();

            return new QualityProfile
            {
                UpgradeAllowed = true,
                Cutoff = Quality.Bluray1080p.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                FormatItems = CustomFormatsTestHelpers.GetSampleFormatItems("None"),
                MinFormatScore = 0
            };
        }

        public static Movie Movie(Action<Movie> configure = null)
        {
            var movie = new Movie
            {
                Id = 7,
                Monitored = true,
                MinimumAvailability = MovieStatusType.Announced,
                Path = "/movies/The Matrix (1999)",
                QualityProfile = Profile(),
                MovieMetadata = new MovieMetadata { Title = "The Matrix", Year = 1999, Runtime = 120 }
            };

            configure?.Invoke(movie);

            return movie;
        }

        public static RemoteMovie Remote(Movie movie, Quality quality, string title = "The.Matrix.1999.1080p.BluRay.x264-GRP", ReleaseInfo release = null)
        {
            return new RemoteMovie
            {
                Movie = movie,
                ParsedMovieInfo = new ParsedMovieInfo { Quality = new QualityModel(quality) },
                Release = release ?? new ReleaseInfo { Title = title, Size = 8.Gigabytes(), DownloadProtocol = DownloadProtocol.Torrent },
                CustomFormats = new List<CustomFormat>()
            };
        }
    }

    [TestFixture]
    public class MovieBlocklistSpecificationFixture : CoreTest<MovieBlocklistSpecification>
    {
        [Test]
        public void should_reject_a_blocklisted_release()
        {
            var remote = MovieSpecTestData.Remote(MovieSpecTestData.Movie(), Quality.Bluray1080p);
            Mocker.GetMock<IMovieBlocklistService>().Setup(s => s.Blocklisted(7, remote.Release)).Returns(true);

            var decision = Subject.IsSatisfiedBy(remote, new MovieReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.Blocklisted);
        }

        [Test]
        public void should_accept_a_release_that_is_not_blocklisted()
        {
            var remote = MovieSpecTestData.Remote(MovieSpecTestData.Movie(), Quality.Bluray1080p);

            Subject.IsSatisfiedBy(remote, new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }
    }

    [TestFixture]
    public class MovieQueueSpecificationFixture : CoreTest<MovieQueueSpecification>
    {
        private Movie _movie;

        [SetUp]
        public void SetUp()
        {
            Mocker.Resolve<UpgradableSpecification>();
            _movie = MovieSpecTestData.Movie();

            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(s => s.ParseCustomFormat(It.IsAny<RemoteMovie>(), It.IsAny<long>()))
                  .Returns(new List<CustomFormat>());
        }

        private void GivenQueued(Quality quality, TrackedDownloadState state = TrackedDownloadState.Downloading, int movieId = 7)
        {
            var queuedMovie = movieId == 7 ? _movie : MovieSpecTestData.Movie(m => m.Id = movieId);

            Mocker.GetMock<IQueueService>().Setup(s => s.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue>
            {
                new NzbDrone.Core.Queue.Queue { RemoteMovie = MovieSpecTestData.Remote(queuedMovie, quality), TrackedDownloadState = state, Size = 1000 }
            });
        }

        [Test]
        public void should_accept_when_nothing_is_queued()
        {
            Mocker.GetMock<IQueueService>().Setup(s => s.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue>());

            Subject.IsSatisfiedBy(MovieSpecTestData.Remote(_movie, Quality.Bluray1080p), new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_when_the_queued_release_already_meets_the_cutoff()
        {
            GivenQueued(Quality.Bluray1080p);

            var decision = Subject.IsSatisfiedBy(MovieSpecTestData.Remote(_movie, Quality.Bluray1080p), new MovieReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.QueueCutoffMet);
        }

        [Test]
        public void should_accept_an_upgrade_over_a_queued_release_below_the_cutoff()
        {
            GivenQueued(Quality.DVD);

            Subject.IsSatisfiedBy(MovieSpecTestData.Remote(_movie, Quality.Bluray720p), new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_lower_quality_than_the_queued_release()
        {
            GivenQueued(Quality.Bluray720p);

            var decision = Subject.IsSatisfiedBy(MovieSpecTestData.Remote(_movie, Quality.DVD), new MovieReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.QueueHigherPreference);
        }

        [Test]
        public void should_ignore_a_queued_release_that_is_failed_pending()
        {
            GivenQueued(Quality.Bluray1080p, TrackedDownloadState.FailedPending);

            Subject.IsSatisfiedBy(MovieSpecTestData.Remote(_movie, Quality.Bluray1080p), new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_ignore_queued_releases_of_other_movies()
        {
            GivenQueued(Quality.Bluray1080p, movieId: 99);

            Subject.IsSatisfiedBy(MovieSpecTestData.Remote(_movie, Quality.Bluray1080p), new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }
    }

    [TestFixture]
    public class MovieAlreadyImportedSpecificationFixture : CoreTest<MovieAlreadyImportedSpecification>
    {
        private Movie _movie;
        private RemoteMovie _remote;

        [SetUp]
        public void SetUp()
        {
            _movie = MovieSpecTestData.Movie(m => m.MovieFileId = 3);
            _remote = MovieSpecTestData.Remote(_movie, Quality.Bluray1080p, "The.Matrix.1999.1080p.BluRay.x264-GRP");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EnableCompletedDownloadHandling).Returns(true);
        }

        private void GivenHistory(Quality grabbed, Quality imported, string sourceTitle = "The.Matrix.1999.1080p.BluRay.x264-GRP", string downloadId = "HASH")
        {
            // Newest first, as the service returns it.
            Mocker.GetMock<IMovieHistoryService>().Setup(s => s.GetByMovieId(7)).Returns(new List<MovieHistory>
            {
                new() { EventType = MovieHistoryEventType.DownloadFolderImported, DownloadId = downloadId, Quality = new QualityModel(imported) },
                new() { EventType = MovieHistoryEventType.Grabbed, DownloadId = downloadId, SourceTitle = sourceTitle, Quality = new QualityModel(grabbed) }
            });
        }

        [Test]
        public void should_accept_a_movie_without_a_file()
        {
            _movie.MovieFileId = 0;

            Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_completed_download_handling_is_off()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EnableCompletedDownloadHandling).Returns(false);
            GivenHistory(Quality.Bluray1080p, Quality.DVD);

            Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_the_grabbed_and_imported_quality_match()
        {
            GivenHistory(Quality.Bluray1080p, Quality.Bluray1080p);

            Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_the_same_release_name_when_the_imported_quality_differs()
        {
            GivenHistory(Quality.Bluray1080p, Quality.DVD);

            var decision = Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.AlreadyImportedSameName);
        }

        [Test]
        public void should_reject_the_same_torrent_hash_when_the_imported_quality_differs()
        {
            GivenHistory(Quality.Bluray1080p, Quality.DVD, sourceTitle: "Other.Name", downloadId: "HASH");
            _remote.Release = new TorrentInfo { Title = "Another.Name", InfoHash = "hash", DownloadProtocol = DownloadProtocol.Torrent };

            var decision = Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.AlreadyImportedSameHash);
        }

        [Test]
        public void should_accept_a_different_release()
        {
            GivenHistory(Quality.Bluray1080p, Quality.DVD, sourceTitle: "Other.Name");

            Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }
    }

    [TestFixture]
    public class MovieFreeSpaceSpecificationFixture : CoreTest<MovieFreeSpaceSpecification>
    {
        private RemoteMovie _remote;

        [SetUp]
        public void SetUp()
        {
            _remote = MovieSpecTestData.Remote(MovieSpecTestData.Movie(), Quality.Bluray1080p);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.MinimumFreeSpaceWhenImporting).Returns(100);
        }

        private void GivenFreeSpace(long? bytes)
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetAvailableSpace(It.IsAny<string>())).Returns(bytes);
        }

        [Test]
        public void should_accept_when_the_check_is_skipped()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.SkipFreeSpaceCheckWhenImporting).Returns(true);
            GivenFreeSpace(1.Megabytes());

            Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_the_free_space_is_unknown()
        {
            GivenFreeSpace(null);

            Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_release_larger_than_the_free_space()
        {
            GivenFreeSpace(4.Gigabytes());

            var decision = Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.MinimumFreeSpace);
        }

        [Test]
        public void should_reject_when_less_than_the_minimum_would_be_left()
        {
            GivenFreeSpace(8.Gigabytes() + 50.Megabytes());

            Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_when_enough_would_be_left()
        {
            GivenFreeSpace(9.Gigabytes());

            Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }
    }

    [TestFixture]
    public class MovieAvailabilitySpecificationFixture : CoreTest<MovieAvailabilitySpecification>
    {
        private RemoteMovie GivenMovie(MovieStatusType availability, DateTime? physical)
        {
            var movie = MovieSpecTestData.Movie(m =>
            {
                m.MinimumAvailability = availability;
                m.MovieMetadata.Value.PhysicalRelease = physical;
            });

            return MovieSpecTestData.Remote(movie, Quality.Bluray1080p);
        }

        [Test]
        public void should_reject_a_movie_that_is_not_out_yet()
        {
            var decision = Subject.IsSatisfiedBy(GivenMovie(MovieStatusType.Released, DateTime.UtcNow.AddDays(30)), new MovieReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.Availability);
        }

        [Test]
        public void should_accept_a_movie_that_is_out()
        {
            Subject.IsSatisfiedBy(GivenMovie(MovieStatusType.Released, DateTime.UtcNow.AddDays(-30)), new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_movie_that_is_not_out_when_the_user_asked_for_the_search()
        {
            Subject.IsSatisfiedBy(GivenMovie(MovieStatusType.Released, DateTime.UtcNow.AddDays(30)), new MovieReleaseDecisionInformation(false, null, true)).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_wait_for_the_availability_delay()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AvailabilityDelay).Returns(14);

            Subject.IsSatisfiedBy(GivenMovie(MovieStatusType.Released, DateTime.UtcNow.AddDays(-3)), new MovieReleaseDecisionInformation()).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class MonitoredMovieSpecificationFixture : CoreTest<MonitoredMovieSpecification>
    {
        [Test]
        public void should_reject_an_unmonitored_movie_from_rss()
        {
            var remote = MovieSpecTestData.Remote(MovieSpecTestData.Movie(m => m.Monitored = false), Quality.Bluray1080p);

            var decision = Subject.IsSatisfiedBy(remote, new MovieReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.MovieNotMonitored);
        }

        [Test]
        public void should_accept_a_monitored_movie()
        {
            Subject.IsSatisfiedBy(MovieSpecTestData.Remote(MovieSpecTestData.Movie(), Quality.Bluray1080p), new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_an_unmonitored_movie_when_the_user_asked_for_the_search()
        {
            var remote = MovieSpecTestData.Remote(MovieSpecTestData.Movie(m => m.Monitored = false), Quality.Bluray1080p);

            Subject.IsSatisfiedBy(remote, new MovieReleaseDecisionInformation(false, null, true)).Accepted.Should().BeTrue();
        }
    }

    [TestFixture]
    public class SearchedMovieSpecificationFixture : CoreTest<SearchedMovieSpecification>
    {
        [Test]
        public void should_accept_anything_for_rss()
        {
            Subject.IsSatisfiedBy(MovieSpecTestData.Remote(MovieSpecTestData.Movie(), Quality.Bluray1080p), new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_release_for_another_movie_than_the_one_searched()
        {
            var remote = MovieSpecTestData.Remote(MovieSpecTestData.Movie(), Quality.Bluray1080p);

            var decision = Subject.IsSatisfiedBy(remote, new MovieReleaseDecisionInformation(false, MovieSpecTestData.Movie(m => m.Id = 8)));

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.WrongMovie);
        }

        [Test]
        public void should_accept_a_release_for_the_movie_searched()
        {
            var remote = MovieSpecTestData.Remote(MovieSpecTestData.Movie(), Quality.Bluray1080p);

            Subject.IsSatisfiedBy(remote, new MovieReleaseDecisionInformation(false, MovieSpecTestData.Movie())).Accepted.Should().BeTrue();
        }
    }

    [TestFixture]
    public class MovieProperSpecificationFixture : CoreTest<MovieProperSpecification>
    {
        private Movie _movie;

        [SetUp]
        public void SetUp()
        {
            Mocker.Resolve<UpgradableSpecification>();
            _movie = MovieSpecTestData.Movie(m => m.MovieFile = new MovieFile { Quality = new QualityModel(Quality.Bluray1080p), DateAdded = DateTime.UtcNow });
            Mocker.GetMock<IConfigService>().SetupGet(c => c.DownloadPropersAndRepacks).Returns(ProperDownloadTypes.PreferAndUpgrade);
        }

        private RemoteMovie Proper()
        {
            var remote = MovieSpecTestData.Remote(_movie, Quality.Bluray1080p);
            remote.ParsedMovieInfo.Quality = new QualityModel(Quality.Bluray1080p, new Revision(2));

            return remote;
        }

        [Test]
        public void should_accept_a_proper_for_a_recent_file()
        {
            Subject.IsSatisfiedBy(Proper(), new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_proper_for_an_old_file()
        {
            _movie.MovieFile.DateAdded = DateTime.UtcNow.AddDays(-30);

            var decision = Subject.IsSatisfiedBy(Proper(), new MovieReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.ProperForOldFile);
        }

        [Test]
        public void should_reject_a_proper_when_propers_are_not_downloaded()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.DownloadPropersAndRepacks).Returns(ProperDownloadTypes.DoNotUpgrade);

            Subject.IsSatisfiedBy(Proper(), new MovieReleaseDecisionInformation()).Reason.Should().Be(DownloadRejectionReason.PropersDisabled);
        }

        [Test]
        public void should_skip_the_check_for_searches()
        {
            _movie.MovieFile.DateAdded = DateTime.UtcNow.AddDays(-30);

            Subject.IsSatisfiedBy(Proper(), new MovieReleaseDecisionInformation(false, _movie)).Accepted.Should().BeTrue();
        }
    }

    [TestFixture]
    public class MovieHistorySpecificationFixture : CoreTest<MovieHistorySpecification>
    {
        private Movie _movie;

        [SetUp]
        public void SetUp()
        {
            Mocker.Resolve<UpgradableSpecification>();
            _movie = MovieSpecTestData.Movie();
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EnableCompletedDownloadHandling).Returns(true);
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(s => s.ParseCustomFormat(It.IsAny<MovieHistory>(), It.IsAny<Movie>())).Returns(new List<CustomFormat>());
        }

        private void GivenMostRecent(MovieHistoryEventType type, Quality quality, DateTime date)
        {
            Mocker.GetMock<IMovieHistoryService>()
                  .Setup(s => s.MostRecentForMovie(7, 0))
                  .Returns(new MovieHistory { EventType = type, Quality = new QualityModel(quality), Date = date });
        }

        [Test]
        public void should_accept_when_there_is_no_history()
        {
            Subject.IsSatisfiedBy(MovieSpecTestData.Remote(_movie, Quality.Bluray1080p), new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_skip_the_check_for_searches()
        {
            GivenMostRecent(MovieHistoryEventType.Grabbed, Quality.Bluray1080p, DateTime.UtcNow);

            Subject.IsSatisfiedBy(MovieSpecTestData.Remote(_movie, Quality.Bluray1080p), new MovieReleaseDecisionInformation(false, _movie)).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_when_a_recent_grab_already_meets_the_cutoff()
        {
            GivenMostRecent(MovieHistoryEventType.Grabbed, Quality.Bluray1080p, DateTime.UtcNow.AddMinutes(-5));

            var decision = Subject.IsSatisfiedBy(MovieSpecTestData.Remote(_movie, Quality.Bluray1080p), new MovieReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.HistoryRecentCutoffMet);
        }

        [Test]
        public void should_accept_when_a_recent_grab_is_still_downloading_and_the_release_may_race_it()
        {
            GivenMostRecent(MovieHistoryEventType.Grabbed, Quality.Bluray1080p, DateTime.UtcNow.AddMinutes(-5));

            var remote = MovieSpecTestData.Remote(_movie, Quality.Bluray1080p);
            Mocker.GetMock<IMovieRacePolicy>().Setup(p => p.MayRace(remote)).Returns(true);

            Subject.IsSatisfiedBy(remote, new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_only_look_at_the_history_of_the_version_being_decided()
        {
            var view = _movie.ShallowCopy();
            view.Slot = new NzbDrone.Core.Movies.Versions.VersionSlot { Id = 5, MovieId = _movie.Id };

            Mocker.GetMock<IMovieHistoryService>()
                  .Setup(s => s.MostRecentForMovie(7, 5))
                  .Returns(new MovieHistory { EventType = MovieHistoryEventType.Grabbed, Quality = new QualityModel(Quality.Bluray1080p), Date = DateTime.UtcNow.AddMinutes(-5) });

            // The movie's own grab (version 0) is not what is looked at for the version.
            Subject.IsSatisfiedBy(MovieSpecTestData.Remote(_movie, Quality.Bluray1080p), new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(MovieSpecTestData.Remote(view, Quality.Bluray1080p), new MovieReleaseDecisionInformation()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_an_old_grab_when_completed_download_handling_is_on()
        {
            GivenMostRecent(MovieHistoryEventType.Grabbed, Quality.Bluray1080p, DateTime.UtcNow.AddDays(-2));

            Subject.IsSatisfiedBy(MovieSpecTestData.Remote(_movie, Quality.Bluray1080p), new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_lower_quality_than_a_recent_grab_below_the_cutoff()
        {
            GivenMostRecent(MovieHistoryEventType.Grabbed, Quality.Bluray720p, DateTime.UtcNow.AddMinutes(-5));

            var decision = Subject.IsSatisfiedBy(MovieSpecTestData.Remote(_movie, Quality.DVD), new MovieReleaseDecisionInformation());

            decision.Reason.Should().Be(DownloadRejectionReason.HistoryHigherPreference);
        }

        [Test]
        public void should_accept_an_upgrade_over_a_recent_grab_below_the_cutoff()
        {
            GivenMostRecent(MovieHistoryEventType.Grabbed, Quality.DVD, DateTime.UtcNow.AddMinutes(-5));

            Subject.IsSatisfiedBy(MovieSpecTestData.Remote(_movie, Quality.Bluray720p), new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }
    }

    [TestFixture]
    public class MovieIndexerTagSpecificationFixture : CoreTest<MovieIndexerTagSpecification>
    {
        private RemoteMovie _remote;

        [SetUp]
        public void SetUp()
        {
            _remote = MovieSpecTestData.Remote(MovieSpecTestData.Movie(m => m.Tags = new HashSet<int> { 1 }), Quality.Bluray1080p);
            _remote.Release.IndexerId = 4;
        }

        private void GivenIndexerTags(params int[] tags)
        {
            Mocker.GetMock<IIndexerFactory>().Setup(f => f.Get(4)).Returns(new IndexerDefinition { Id = 4, Tags = new HashSet<int>(tags) });
        }

        [Test]
        public void should_accept_an_indexer_without_tags()
        {
            GivenIndexerTags();

            Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_a_tag_is_shared()
        {
            GivenIndexerTags(1, 2);

            Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_when_no_tag_is_shared()
        {
            GivenIndexerTags(2);

            var decision = Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.NoMatchingTag);
        }

        [Test]
        public void should_accept_a_release_with_no_known_indexer()
        {
            _remote.Release.IndexerId = 0;

            Subject.IsSatisfiedBy(_remote, new MovieReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }
    }
}
