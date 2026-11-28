using System.Collections.Generic;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Requests;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.RequestTests
{
    [TestFixture]
    public class MediaRequestServiceFixture : CoreTest<MediaRequestService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.All())
                  .Returns(new List<RootFolder>
                  {
                      new RootFolder { Id = 1, Path = "/media/any", Library = LibraryType.Any },
                      new RootFolder { Id = 2, Path = "/media/movies", Library = LibraryType.Movies },
                      new RootFolder { Id = 3, Path = "/media/tv", Library = LibraryType.Series }
                  });

            Mocker.GetMock<IQualityProfileService>()
                  .Setup(s => s.All())
                  .Returns(new List<QualityProfile> { new QualityProfile { Id = 7 }, new QualityProfile { Id = 4 } });

            Mocker.GetMock<IAddSeriesService>()
                  .Setup(s => s.AddSeries(It.IsAny<Series>()))
                  .Returns<Series>(s =>
                  {
                      s.Id = 10;
                      s.Title = "Added";
                      return s;
                  });

            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovie(It.IsAny<Movie>()))
                  .Returns<Movie>(m =>
                  {
                      m.Id = 20;
                      return m;
                  });
        }

        [Test]
        public void should_return_existing_series_without_adding()
        {
            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.FindByTvdbId(123))
                  .Returns(new Series { Id = 5, Title = "There", TvdbId = 123 });

            var result = Subject.Request(new MediaRequest { MediaType = RequestMediaType.Series, ExternalId = 123 });

            result.AlreadyExisted.Should().BeTrue();
            result.Id.Should().Be(5);
            Mocker.GetMock<IAddSeriesService>().Verify(s => s.AddSeries(It.IsAny<Series>()), Times.Never());
        }

        [Test]
        public void should_add_series_with_defaults()
        {
            var result = Subject.Request(new MediaRequest { MediaType = RequestMediaType.Series, ExternalId = 123 });

            result.AlreadyExisted.Should().BeFalse();
            result.Id.Should().Be(10);

            Mocker.GetMock<IAddSeriesService>()
                  .Verify(s => s.AddSeries(It.Is<Series>(x => x.TvdbId == 123 && x.RootFolderPath == "/media/tv" && x.QualityProfileId == 4 && x.Monitored && x.AddOptions.Monitor == Tv.MonitorTypes.All && x.AddOptions.SearchForMissingEpisodes)),
                          Times.Once());
        }

        [Test]
        public void should_not_search_and_not_monitor_when_asked()
        {
            Subject.Request(new MediaRequest
            {
                MediaType = RequestMediaType.Series,
                ExternalId = 123,
                SearchNow = false,
                SeriesMonitor = Tv.MonitorTypes.None
            });

            Mocker.GetMock<IAddSeriesService>()
                  .Verify(s => s.AddSeries(It.Is<Series>(x => !x.Monitored && !x.AddOptions.SearchForMissingEpisodes)), Times.Once());
        }

        [Test]
        public void should_add_movie_to_movies_root_folder()
        {
            var result = Subject.Request(new MediaRequest { MediaType = RequestMediaType.Movie, ExternalId = 603 });

            result.Id.Should().Be(20);

            Mocker.GetMock<IAddMovieService>()
                  .Verify(s => s.AddMovie(It.Is<Movie>(x => x.TmdbId == 603 && x.RootFolderPath == "/media/movies" && x.QualityProfileId == 4 && x.AddOptions.SearchForMovie)),
                          Times.Once());
        }

        [Test]
        public void should_fall_back_to_any_root_folder()
        {
            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.All())
                  .Returns(new List<RootFolder> { new RootFolder { Id = 1, Path = "/media/any", Library = LibraryType.Any } });

            Subject.Request(new MediaRequest { MediaType = RequestMediaType.Movie, ExternalId = 603 });

            Mocker.GetMock<IAddMovieService>()
                  .Verify(s => s.AddMovie(It.Is<Movie>(x => x.RootFolderPath == "/media/any")), Times.Once());
        }

        [Test]
        public void should_refuse_root_folder_of_other_library()
        {
            Assert.Throws<ValidationException>(() => Subject.Request(new MediaRequest
            {
                MediaType = RequestMediaType.Movie,
                ExternalId = 603,
                RootFolderPath = "/media/tv"
            }));
        }

        [Test]
        public void should_refuse_unknown_quality_profile()
        {
            Assert.Throws<ValidationException>(() => Subject.Request(new MediaRequest
            {
                MediaType = RequestMediaType.Movie,
                ExternalId = 603,
                QualityProfileId = 99
            }));
        }

        [Test]
        public void should_refuse_missing_id()
        {
            Assert.Throws<ValidationException>(() => Subject.Request(new MediaRequest { MediaType = RequestMediaType.Movie }));
        }
    }

    [TestFixture]
    public class LibraryStatusFixture
    {
        [TestCase(true, 0, 0, LibraryItemStatus.Wanted)]
        [TestCase(false, 0, 5, LibraryItemStatus.Unmonitored)]
        [TestCase(true, 2, 5, LibraryItemStatus.Partial)]
        [TestCase(true, 5, 5, LibraryItemStatus.Available)]
        [TestCase(false, 5, 5, LibraryItemStatus.Available)]
        public void series_status(bool monitored, int files, int episodes, LibraryItemStatus expected)
        {
            LibraryStatusService.SeriesStatus(monitored, files, episodes).Should().Be(expected);
        }

        [Test]
        public void movie_downloading_wins()
        {
            LibraryStatusService.MovieStatus(new Movie { Monitored = true, MovieFileId = 3 }, true).Should().Be(LibraryItemStatus.Downloading);
        }

        [Test]
        public void movie_with_file_is_available()
        {
            LibraryStatusService.MovieStatus(new Movie { Monitored = true, MovieFileId = 3 }, false).Should().Be(LibraryItemStatus.Available);
        }

        [Test]
        public void monitored_movie_without_file_is_wanted()
        {
            LibraryStatusService.MovieStatus(new Movie { Monitored = true }, false).Should().Be(LibraryItemStatus.Wanted);
        }
    }
}
