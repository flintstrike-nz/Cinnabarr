using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Versions;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    [TestFixture]
    public class EpisodeSearchServiceFixture : CoreTest<EpisodeSearchService>
    {
        private Series _versionView;
        private List<Episode> _versionEpisodes;

        [SetUp]
        public void Setup()
        {
            _versionView = new Series { Id = 5, Slot = new SeriesVersionSlot { Id = 7, SeriesId = 5, Label = "UHD" } };
            _versionEpisodes = new List<Episode> { new Episode { Id = 10, SeriesId = 5, SeasonNumber = 1, EpisodeNumber = 1 } };

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodeBySeries(5))
                  .Returns(new List<Episode>());

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.EpisodesWithoutFiles(It.IsAny<PagingSpec<Episode>>(), It.IsAny<bool>(), It.IsAny<HashSet<int>>()))
                  .Returns(new PagingSpec<Episode> { Records = new List<Episode>() });

            Mocker.GetMock<IQueueService>().Setup(q => q.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue>());

            Mocker.GetMock<ISearchForReleases>()
                  .Setup(s => s.EpisodeSearch(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .ReturnsAsync(new List<DownloadDecision>());

            Mocker.GetMock<IProcessDownloadDecisions>()
                  .Setup(s => s.ProcessDecisions(It.IsAny<List<DownloadDecision>>()))
                  .ReturnsAsync(new ProcessedDecisions(new List<DownloadDecision>(), new List<DownloadDecision>(), new List<DownloadDecision>()));
        }

        private void GivenMissingVersion()
        {
            Mocker.GetMock<ISeriesVersionSlotService>()
                  .Setup(s => s.GetMissing(It.IsAny<ICollection<int>>()))
                  .Returns(new List<SeriesVersionWanted> { new SeriesVersionWanted { Series = _versionView, Episodes = _versionEpisodes } });
        }

        [Test]
        public void should_search_for_the_episodes_a_version_is_missing()
        {
            GivenMissingVersion();

            Subject.Execute(new MissingEpisodeSearchCommand { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>().Verify(s => s.EpisodeSearch(10, 7, true, false), Times.Once());
        }

        [Test]
        public void should_not_search_a_version_for_an_episode_already_downloading_for_that_version()
        {
            GivenMissingVersion();

            var queued = new RemoteEpisode { Series = _versionView, Episodes = _versionEpisodes };

            Mocker.GetMock<IQueueService>().Setup(q => q.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue>
            {
                new NzbDrone.Core.Queue.Queue { Id = 1, Series = _versionView, Episodes = _versionEpisodes, RemoteEpisode = queued, TrackedDownloadState = NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadState.Downloading }
            });

            Subject.Execute(new MissingEpisodeSearchCommand { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>().Verify(s => s.EpisodeSearch(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_not_search_versions_when_only_unmonitored_episodes_are_asked_for()
        {
            GivenMissingVersion();

            Subject.Execute(new MissingEpisodeSearchCommand { Monitored = false });

            Mocker.GetMock<ISeriesVersionSlotService>().Verify(s => s.GetMissing(It.IsAny<ICollection<int>>()), Times.Never());
        }

        [Test]
        public void should_limit_the_versions_to_the_series_of_the_command()
        {
            Mocker.GetMock<ISeriesVersionSlotService>()
                  .Setup(s => s.GetMissing(It.IsAny<ICollection<int>>()))
                  .Returns(new List<SeriesVersionWanted>());

            Subject.Execute(new MissingEpisodeSearchCommand(5));

            Mocker.GetMock<ISeriesVersionSlotService>().Verify(s => s.GetMissing(It.Is<ICollection<int>>(ids => ids.SequenceEqual(new[] { 5 }))), Times.Once());
        }
    }
}
