using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class TrackedMovieDownloadFixture : CoreTest<TrackedDownloadService>
    {
        private const string Hash = "0123456789ABCDEF0123456789ABCDEF01234567";

        private DownloadClientDefinition _client;
        private Movie _movie;

        [SetUp]
        public void SetUp()
        {
            _client = new DownloadClientDefinition { Id = 1, Name = "Cinnabarr Engine", Protocol = DownloadProtocol.Torrent };
            _movie = new Movie { Id = 8, MovieMetadata = new MovieMetadata { TmdbId = 603, Title = "The Matrix", Year = 1999 } };

            Mocker.GetMock<IEngineClientRegistry>().Setup(s => s.IsEngineClient(1)).Returns(true);
            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId(Hash)).Returns(new List<EpisodeHistory>());

            Mocker.GetMock<IMovieHistoryService>()
                  .Setup(s => s.FindByDownloadId(Hash))
                  .Returns(new List<MovieHistory>
                  {
                      new()
                      {
                          MovieId = 8,
                          EventType = MovieHistoryEventType.Grabbed,
                          DownloadId = Hash,
                          Date = DateTime.UtcNow,
                          SourceTitle = "The.Matrix.1999.1080p.BluRay.x264-GRP",
                          Quality = new QualityModel(Quality.Bluray1080p),
                          Languages = new List<Language> { Language.English },
                          Data = { ["indexer"] = "Site" }
                      }
                  });

            Mocker.GetMock<IMovieParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedMovieInfo>(), 8))
                  .Returns<ParsedMovieInfo, int>((parsed, _) => new RemoteMovie { ParsedMovieInfo = parsed, Movie = _movie });

            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(s => s.ParseCustomFormat(It.IsAny<RemoteMovie>(), It.IsAny<long>()))
                  .Returns(new List<CustomFormat>());
        }

        private DownloadClientItem Item() => new()
        {
            Title = "The.Matrix.1999.1080p.BluRay.x264-GRP",
            DownloadId = Hash,
            TotalSize = 5000,
            DownloadClientInfo = new DownloadClientItemClientInfo { Protocol = _client.Protocol, Id = _client.Id, Name = _client.Name }
        };

        [Test]
        public void should_track_an_engine_torrent_grabbed_for_a_movie_with_its_movie()
        {
            var tracked = Subject.TrackDownload(_client, Item());

            tracked.RemoteMovie.Should().NotBeNull();
            tracked.RemoteMovie.Movie.Should().BeSameAs(_movie);
            tracked.RemoteMovie.ParsedMovieInfo.Quality.Quality.Should().Be(Quality.Bluray1080p);
            tracked.RemoteMovie.Languages.Should().ContainSingle().Which.Should().Be(Language.English);
            tracked.RemoteMovie.Release.Indexer.Should().Be("Site");
            tracked.RemoteEpisode.Should().BeNull();
            tracked.IsUnlinked.Should().BeFalse();
            tracked.Indexer.Should().Be("Site");
        }

        [Test]
        public void should_not_call_a_movie_torrent_unlinked_or_look_for_a_series()
        {
            var tracked = Subject.TrackDownload(_client, Item());

            tracked.IsUnlinked.Should().BeFalse();
            Mocker.GetMock<IParsingService>().Verify(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_list_a_movie_download_without_a_movie_when_the_movie_is_gone()
        {
            Mocker.GetMock<IMovieParsingService>().Setup(s => s.Map(It.IsAny<ParsedMovieInfo>(), 8)).Throws(new InvalidOperationException("gone"));

            var tracked = Subject.TrackDownload(_client, Item());

            tracked.RemoteMovie.Should().BeNull();
            tracked.RemoteEpisode.Should().BeNull();
        }

        [Test]
        public void should_mark_an_engine_torrent_with_neither_kind_of_history_as_unlinked()
        {
            Mocker.GetMock<IMovieHistoryService>().Setup(s => s.FindByDownloadId(Hash)).Returns(new List<MovieHistory>());

            Subject.TrackDownload(_client, Item()).IsUnlinked.Should().BeTrue();
        }

        [Test]
        public void should_list_a_missing_engine_grab_for_a_movie_with_its_movie()
        {
            var grab = new DownloadHistory
            {
                DownloadId = Hash,
                MovieId = 8,
                SourceTitle = "The.Matrix.1999.1080p.BluRay.x264-GRP",
                Release = new ReleaseInfo { Size = 5000 },
                Date = DateTime.UtcNow
            };

            var tracked = Subject.TrackMissingFromEngine(_client, grab);

            tracked.IsMissingFromEngine.Should().BeTrue();
            tracked.RemoteMovie.Should().NotBeNull();
            tracked.RemoteMovie.Movie.Id.Should().Be(8);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_drop_the_movie_of_a_tracked_download_when_it_is_deleted()
        {
            var tracked = Subject.TrackDownload(_client, Item());
            tracked.RemoteMovie.Should().NotBeNull();

            Mocker.GetMock<IMovieHistoryService>().Setup(s => s.FindByDownloadId(Hash)).Returns(new List<MovieHistory>());

            Subject.Handle(new MoviesDeletedEvent(new List<Movie> { _movie }, false, false));

            tracked.RemoteMovie.Should().BeNull();
            tracked.IsUnlinked.Should().BeTrue();
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<TrackedDownloadRefreshedEvent>()), Times.Once());
        }
    }
}
