using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.QueueTests
{
    [TestFixture]
    public class QueueServiceFixture : CoreTest<QueueService>
    {
        private List<TrackedDownload> _trackedDownloads;

        [SetUp]
        public void SetUp()
        {
            var downloadClientInfo = Builder<DownloadClientItemClientInfo>.CreateNew().Build();

            var downloadItem = Builder<NzbDrone.Core.Download.DownloadClientItem>.CreateNew()
                                        .With(v => v.RemainingTime = TimeSpan.FromSeconds(10))
                                        .With(v => v.DownloadClientInfo = downloadClientInfo)
                                        .Build();

            var series = Builder<Series>.CreateNew()
                                        .Build();

            var episodes = Builder<Episode>.CreateListOfSize(3)
                                          .All()
                                          .With(e => e.SeriesId = series.Id)
                                          .Build();

            var remoteEpisode = Builder<RemoteEpisode>.CreateNew()
                                                   .With(r => r.Series = series)
                                                   .With(r => r.Episodes = new List<Episode>(episodes))
                                                   .With(r => r.ParsedEpisodeInfo = new ParsedEpisodeInfo())
                                                   .Build();

            _trackedDownloads = Builder<TrackedDownload>.CreateListOfSize(1)
                .All()
                .With(v => v.IsTrackable = true)
                .With(v => v.DownloadItem = downloadItem)
                .With(v => v.RemoteEpisode = remoteEpisode)
                .Build()
                .ToList();
        }

        [Test]
        public void queue_items_should_have_id()
        {
            Subject.Handle(new TrackedDownloadRefreshedEvent(_trackedDownloads));

            var queue = Subject.GetQueue();

            queue.Should().HaveCount(1);

            queue.All(v => v.Id > 0).Should().BeTrue();
        }

        [Test]
        public void should_list_untrackable_download_from_built_in_engine()
        {
            _trackedDownloads[0].IsTrackable = false;
            _trackedDownloads[0].State = TrackedDownloadState.Imported;
            _trackedDownloads[0].DownloadClient = 5;

            Mocker.GetMock<IEngineClientRegistry>()
                .Setup(s => s.IsEngineClient(5))
                .Returns(true);

            Subject.Handle(new TrackedDownloadRefreshedEvent(_trackedDownloads));

            var queue = Subject.GetQueue();
            queue.Should().HaveCount(1);
            queue.Should().OnlyContain(q => !q.IsTrackable);
        }

        [Test]
        public void should_not_list_untrackable_download_from_other_clients()
        {
            _trackedDownloads[0].IsTrackable = false;
            _trackedDownloads[0].State = TrackedDownloadState.Imported;
            _trackedDownloads[0].DownloadClient = 6;

            Mocker.GetMock<IEngineClientRegistry>()
                .Setup(s => s.IsEngineClient(It.IsAny<int>()))
                .Returns(false);

            Subject.Handle(new TrackedDownloadRefreshedEvent(_trackedDownloads));

            Subject.GetQueue().Should().BeEmpty();
        }

        [Test]
        public void should_map_a_movie_download_with_its_movie_quality_and_languages()
        {
            var movie = new NzbDrone.Core.Movies.Movie { Id = 5, MovieMetadata = new NzbDrone.Core.Movies.MovieMetadata { Title = "The Matrix", Year = 1999 } };

            _trackedDownloads[0].RemoteEpisode = null;
            _trackedDownloads[0].RemoteMovie = new RemoteMovie
            {
                Movie = movie,
                ParsedMovieInfo = new ParsedMovieInfo { Quality = new NzbDrone.Core.Qualities.QualityModel(NzbDrone.Core.Qualities.Quality.Bluray1080p) },
                Languages = new List<NzbDrone.Core.Languages.Language> { NzbDrone.Core.Languages.Language.English }
            };

            Subject.Handle(new TrackedDownloadRefreshedEvent(_trackedDownloads));

            var queue = Subject.GetQueue();

            queue.Should().ContainSingle();
            queue[0].Movie.Should().BeSameAs(movie);
            queue[0].Series.Should().BeNull();
            queue[0].Episodes.Should().BeEmpty();
            queue[0].RemoteMovie.Should().BeSameAs(_trackedDownloads[0].RemoteMovie);
            queue[0].Quality.Quality.Should().Be(NzbDrone.Core.Qualities.Quality.Bluray1080p);
            queue[0].Languages.Should().ContainSingle().Which.Should().Be(NzbDrone.Core.Languages.Language.English);
        }
    }
}
