using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineSeedingServiceFixture : CoreTest<EngineSeedingService>
    {
        private const string Hash = "abcdef0123456789abcdef0123456789abcdef01";

        private QBittorrentSettings _settings;
        private Mock<IQBittorrentProxy> _proxy;
        private QBittorrentTorrent _torrent;
        private EngineLink _link;
        private TorrentSeedConfiguration _trackerMinimum;

        [SetUp]
        public void Setup()
        {
            _settings = new QBittorrentSettings();
            _proxy = new Mock<IQBittorrentProxy>();
            _trackerMinimum = null;
            _torrent = new QBittorrentTorrent { Hash = Hash, Name = "Show", Ratio = 0.2f, RatioLimit = -2, SeedingTimeLimit = -2 };
            _link = new EngineLink
            {
                Completed = true,
                Files = new List<EngineLinkFile>
                {
                    new EngineLinkFile { Index = 0, State = EngineLinkFileState.Imported, ImportedAt = DateTime.UtcNow.AddDays(-1) },
                    new EngineLinkFile { Index = 1, IsSubtitle = true, VideoIndex = 0, State = EngineLinkFileState.Imported }
                }
            };

            _proxy.Setup(p => p.GetTorrents(_settings)).Returns(() => new List<QBittorrentTorrent> { _torrent });
            Mocker.GetMock<IQBittorrentProxySelector>().Setup(s => s.GetProxy(_settings, false)).Returns(_proxy.Object);
            Mocker.GetMock<IEngineLinkService>().Setup(s => s.Get(Hash.ToUpperInvariant())).Returns(() => _link);

            GivenGlobalGoal(EngineSeedingMode.Either, 14 * 24 * 60, 1.0);

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestGrab(Hash.ToUpperInvariant()))
                .Returns(new DownloadHistory { SourceTitle = "Show.S01E01.720p.HDTV-Grp", Release = new ReleaseInfo { IndexerId = 4 } });

            Mocker.GetMock<ISeedConfigProvider>().Setup(s => s.GetSeedConfiguration(4, false)).Returns(() => _trackerMinimum);
        }

        private void GivenGlobalGoal(EngineSeedingMode mode, int minutes, double ratio)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EngineSeedingMode).Returns((int)mode);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EngineSeedingMinutes).Returns(minutes);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EngineSeedingRatio).Returns(ratio);
        }

        private IDownloadClient GivenClient()
        {
            var client = new Mock<IDownloadClient>();
            client.SetupGet(c => c.Definition).Returns(new DownloadClientDefinition { Id = 3, Settings = _settings });

            return client.Object;
        }

        private void VerifyRemoved(bool removed)
        {
            _proxy.Verify(p => p.RemoveTorrent(Hash, false, _settings), removed ? Times.Once() : Times.Never());
            _proxy.Verify(p => p.RemoveTorrent(It.IsAny<string>(), true, It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_keep_seeding_until_the_goal_is_met()
        {
            Subject.Process(GivenClient());

            VerifyRemoved(false);
        }

        [Test]
        public void should_remove_the_torrent_and_keep_its_files_once_the_goal_is_met()
        {
            _torrent.Ratio = 1.2f;

            Subject.Process(GivenClient());

            VerifyRemoved(true);
        }

        [Test]
        public void should_count_seeding_time_from_when_each_file_was_imported()
        {
            GivenGlobalGoal(EngineSeedingMode.Time, 24 * 60, 0);
            _link.Files.Add(new EngineLinkFile { Index = 2, State = EngineLinkFileState.Imported, ImportedAt = DateTime.UtcNow.AddHours(-2) });

            Subject.Process(GivenClient());

            // The second video was imported later and hasn't seeded long enough yet.
            VerifyRemoved(false);
        }

        [Test]
        public void should_remove_straight_away_when_the_goal_is_not_to_seed()
        {
            GivenGlobalGoal(EngineSeedingMode.None, 0, 0);

            Subject.Process(GivenClient());

            VerifyRemoved(true);
        }

        [Test]
        public void should_hold_to_a_trackers_minimum()
        {
            GivenGlobalGoal(EngineSeedingMode.None, 0, 0);
            _trackerMinimum = new TorrentSeedConfiguration { SeedTime = TimeSpan.FromDays(3) };

            Subject.Process(GivenClient());

            VerifyRemoved(false);
            Subject.GetRules(Hash.ToUpperInvariant()).Should().Contain(r => r.Source == "tracker" && r.Mode == EngineSeedingMode.Time && r.Minutes == 3 * 24 * 60);
        }

        [Test]
        public void should_require_both_the_time_and_the_ratio_in_both_mode()
        {
            GivenGlobalGoal(EngineSeedingMode.Both, 60, 1.0);
            _torrent.Ratio = 0.5f;

            Subject.Process(GivenClient());

            VerifyRemoved(false);
        }

        [Test]
        public void should_leave_torrents_that_are_not_finished()
        {
            GivenGlobalGoal(EngineSeedingMode.None, 0, 0);
            _link.Completed = false;

            Subject.Process(GivenClient());

            VerifyRemoved(false);
        }

        [Test]
        public void should_write_a_single_time_or_ratio_goal_to_the_engines_share_limits_once()
        {
            Subject.Process(GivenClient());

            _proxy.Verify(p => p.SetTorrentSeedingConfiguration(Hash, It.Is<TorrentSeedConfiguration>(c => c.Ratio == 1.0 && c.SeedTime == TimeSpan.FromDays(14)), _settings), Times.Once());

            _torrent.RatioLimit = 1.0f;
            _torrent.SeedingTimeLimit = 14 * 24 * 60;
            Subject.Process(GivenClient());

            _proxy.Verify(p => p.SetTorrentSeedingConfiguration(It.IsAny<string>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>()), Times.Once());
        }

        [Test]
        public void should_leave_the_engine_unlimited_when_its_limits_would_stop_seeding_too_early()
        {
            // The engine stops at whichever limit comes first, which can't express a goal plus a tracker's minimum.
            _trackerMinimum = new TorrentSeedConfiguration { SeedTime = TimeSpan.FromDays(3) };

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.SetTorrentSeedingConfiguration(Hash, It.Is<TorrentSeedConfiguration>(c => c.Ratio == -1 && c.SeedTime == TimeSpan.FromMinutes(-1)), _settings), Times.Once());
        }
    }
}
