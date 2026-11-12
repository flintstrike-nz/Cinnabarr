using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Download;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Versions;
using Sonarr.Api.V5.Search;

namespace NzbDrone.Api.Test.v5.Search
{
    [TestFixture]
    public class SearchGrabVersionFixture
    {
        private Mock<IVersionSlotService> _slots;
        private Mock<IDownloadService> _downloads;
        private Movie _movie;
        private Movie _view;
        private RemoteMovie _remote;
        private Movie _movieAtGrab;
        private SearchController _controller;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie { Id = 7 };
            _view = new Movie { Id = 7 };
            _remote = new RemoteMovie { Movie = _movie, Release = new ReleaseInfo { Guid = "g", IndexerId = 2 } };
            _movieAtGrab = null;

            var cache = new Mock<ICached<RemoteMedia>>();
            cache.Setup(c => c.Find("2_g")).Returns(_remote);

            var cacheManager = new Mock<ICacheManager>();
            cacheManager.Setup(c => c.GetCache<RemoteMedia>(It.IsAny<System.Type>(), It.IsAny<string>())).Returns(cache.Object);

            _slots = new Mock<IVersionSlotService>();
            _slots.Setup(s => s.GetView(_movie, It.IsAny<int>())).Returns(_view);

            _downloads = new Mock<IDownloadService>();
            _downloads.Setup(d => d.DownloadReport(It.IsAny<RemoteMedia>(), It.IsAny<int?>()))
                      .Callback<RemoteMedia, int?>((remote, _) => _movieAtGrab = ((RemoteMovie)remote).Movie)
                      .Returns(System.Threading.Tasks.Task.CompletedTask);

            _controller = new SearchController(Mock.Of<ISearchForFreeText>(), _slots.Object, Mock.Of<ISeriesVersionSlotService>(), _downloads.Object, cacheManager.Object, Mock.Of<Logger>());
        }

        [Test]
        public async System.Threading.Tasks.Task should_grab_for_the_version_asked_for_and_leave_the_cached_release_as_it_was()
        {
            await _controller.Grab(new SearchGrabResource { Guid = "g", IndexerId = 2, VersionSlotId = 5 });

            _slots.Verify(s => s.GetView(_movie, 5), Times.Once());
            _movieAtGrab.Should().BeSameAs(_view);
            _remote.Movie.Should().BeSameAs(_movie);
        }

        [Test]
        public async System.Threading.Tasks.Task should_make_a_new_version_and_grab_for_it()
        {
            _slots.Setup(s => s.Add(It.Is<VersionSlot>(v => v.MovieId == 7 && v.Label == "4K" && v.QualityProfileId == 9 && v.Monitored))).Returns(new VersionSlot { Id = 12 });

            await _controller.Grab(new SearchGrabResource { Guid = "g", IndexerId = 2, NewVersionLabel = "4K", NewVersionQualityProfileId = 9 });

            _slots.Verify(s => s.GetView(_movie, 12), Times.Once());
            _movieAtGrab.Should().BeSameAs(_view);
        }

        [Test]
        public async System.Threading.Tasks.Task should_grab_for_the_movie_itself_without_a_version()
        {
            await _controller.Grab(new SearchGrabResource { Guid = "g", IndexerId = 2 });

            _slots.Verify(s => s.GetView(It.IsAny<Movie>(), It.IsAny<int>()), Times.Never());
            _movieAtGrab.Should().BeSameAs(_movie);
        }
    }

    [TestFixture]
    public class SearchGrabSeriesVersionFixture
    {
        private Mock<ISeriesVersionSlotService> _slots;
        private Mock<IDownloadService> _downloads;
        private Series _series;
        private Series _view;
        private List<Episode> _episodes;
        private List<Episode> _episodeViews;
        private RemoteEpisode _remote;
        private Series _seriesAtGrab;
        private List<Episode> _episodesAtGrab;
        private SearchController _controller;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Id = 5 };
            _view = new Series { Id = 5 };
            _episodes = new List<Episode> { new Episode { Id = 10 } };
            _episodeViews = new List<Episode> { new Episode { Id = 10 } };
            _remote = new RemoteEpisode { Series = _series, Episodes = _episodes, Release = new ReleaseInfo { Guid = "g", IndexerId = 2 } };
            _seriesAtGrab = null;
            _episodesAtGrab = null;

            var cache = new Mock<ICached<RemoteMedia>>();
            cache.Setup(c => c.Find("2_g")).Returns(_remote);

            var cacheManager = new Mock<ICacheManager>();
            cacheManager.Setup(c => c.GetCache<RemoteMedia>(It.IsAny<System.Type>(), It.IsAny<string>())).Returns(cache.Object);

            _slots = new Mock<ISeriesVersionSlotService>();
            _slots.Setup(s => s.GetView(_series, It.IsAny<int>())).Returns(_view);
            _slots.Setup(s => s.GetEpisodeViews(_episodes, It.IsAny<int>())).Returns(_episodeViews);

            _downloads = new Mock<IDownloadService>();
            _downloads.Setup(d => d.DownloadReport(It.IsAny<RemoteMedia>(), It.IsAny<int?>()))
                      .Callback<RemoteMedia, int?>((remote, _) =>
                      {
                          _seriesAtGrab = ((RemoteEpisode)remote).Series;
                          _episodesAtGrab = ((RemoteEpisode)remote).Episodes;
                      })
                      .Returns(System.Threading.Tasks.Task.CompletedTask);

            _controller = new SearchController(Mock.Of<ISearchForFreeText>(), Mock.Of<IVersionSlotService>(), _slots.Object, _downloads.Object, cacheManager.Object, Mock.Of<Logger>());
        }

        [Test]
        public async System.Threading.Tasks.Task should_grab_for_the_version_asked_for_and_leave_the_cached_release_as_it_was()
        {
            await _controller.Grab(new SearchGrabResource { Guid = "g", IndexerId = 2, VersionSlotId = 5 });

            _seriesAtGrab.Should().BeSameAs(_view);
            _episodesAtGrab.Should().BeSameAs(_episodeViews);
            _remote.Series.Should().BeSameAs(_series);
            _remote.Episodes.Should().BeSameAs(_episodes);
        }

        [Test]
        public async System.Threading.Tasks.Task should_make_a_new_version_and_grab_for_it()
        {
            _slots.Setup(s => s.Add(It.Is<SeriesVersionSlot>(v => v.SeriesId == 5 && v.Label == "4K" && v.QualityProfileId == 9 && v.Monitored))).Returns(new SeriesVersionSlot { Id = 12 });

            await _controller.Grab(new SearchGrabResource { Guid = "g", IndexerId = 2, NewVersionLabel = "4K", NewVersionQualityProfileId = 9 });

            _slots.Verify(s => s.GetView(_series, 12), Times.Once());
            _seriesAtGrab.Should().BeSameAs(_view);
        }

        [Test]
        public async System.Threading.Tasks.Task should_grab_for_the_series_itself_without_a_version()
        {
            await _controller.Grab(new SearchGrabResource { Guid = "g", IndexerId = 2 });

            _slots.Verify(s => s.GetView(It.IsAny<Series>(), It.IsAny<int>()), Times.Never());
            _seriesAtGrab.Should().BeSameAs(_series);
        }
    }
}
