using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Movies;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests
{
    [TestFixture]
    public class RssSyncServiceMovieFixture : CoreTest<RssSyncService>
    {
        private List<ReleaseInfo> _seriesReleases;
        private List<ReleaseInfo> _movieReleases;
        private List<DownloadDecision> _seriesDecisions;
        private List<DownloadDecision> _movieDecisions;

        [SetUp]
        public void SetUp()
        {
            _seriesReleases = new List<ReleaseInfo> { new() { Title = "Show.S01E01.1080p.WEB-DL-GRP" } };
            _movieReleases = new List<ReleaseInfo> { new() { Title = "The.Matrix.1999.1080p.BluRay.x264-GRP" } };
            _seriesDecisions = new List<DownloadDecision> { new(new RemoteEpisode { Release = _seriesReleases[0] }) };
            _movieDecisions = new List<DownloadDecision> { new(new RemoteMovie { Release = _movieReleases[0] }) };

            Mocker.GetMock<IFetchAndParseRss>().Setup(f => f.Fetch()).ReturnsAsync(_seriesReleases);
            Mocker.GetMock<IFetchAndParseRss>().Setup(f => f.FetchMovies()).ReturnsAsync(_movieReleases);
            Mocker.GetMock<IPendingReleaseService>().Setup(p => p.GetPending()).Returns(new List<ReleaseInfo>());
            Mocker.GetMock<IMakeDownloadDecision>().Setup(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), false)).Returns(_seriesDecisions);
            Mocker.GetMock<IMakeMovieDownloadDecision>().Setup(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), false)).Returns(_movieDecisions);

            Mocker.GetMock<IProcessDownloadDecisions>()
                  .Setup(p => p.ProcessDecisions(It.IsAny<List<DownloadDecision>>()))
                  .Returns<List<DownloadDecision>>(d => Task.FromResult(new ProcessedDecisions(d, new List<DownloadDecision>(), new List<DownloadDecision>())));
        }

        [Test]
        public void should_decide_and_grab_movie_releases_separately_from_series_releases()
        {
            Subject.Execute(new RssSyncCommand());

            Mocker.GetMock<IMakeMovieDownloadDecision>().Verify(d => d.GetRssDecision(_movieReleases, false), Times.Once());
            Mocker.GetMock<IMakeDownloadDecision>().Verify(d => d.GetRssDecision(It.Is<List<ReleaseInfo>>(r => r.Count == 1 && r[0] == _seriesReleases[0]), false), Times.Once());
            Mocker.GetMock<IProcessDownloadDecisions>().Verify(p => p.ProcessDecisions(_movieDecisions), Times.Once());
            Mocker.GetMock<IProcessDownloadDecisions>().Verify(p => p.ProcessDecisions(_seriesDecisions), Times.Once());
        }

        [Test]
        public void should_only_describe_the_series_side_in_the_completion_event()
        {
            Subject.Execute(new RssSyncCommand());

            Mocker.GetMock<IEventAggregator>().Verify(
                e => e.PublishEvent(It.Is<RssSyncCompleteEvent>(c => c.ProcessedDecisions.Grabbed.Count == 1 && c.ProcessedDecisions.Grabbed[0].RemoteEpisode != null)),
                Times.Once());
        }

        [Test]
        public void should_not_run_the_movie_pipeline_when_no_movie_releases_were_found()
        {
            Mocker.GetMock<IFetchAndParseRss>().Setup(f => f.FetchMovies()).ReturnsAsync(new List<ReleaseInfo>());

            Subject.Execute(new RssSyncCommand());

            Mocker.GetMock<IMakeMovieDownloadDecision>().Verify(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<bool>()), Times.Never());
        }
    }
}
