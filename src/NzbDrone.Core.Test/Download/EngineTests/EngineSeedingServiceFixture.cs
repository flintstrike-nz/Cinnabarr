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
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

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
        public void should_list_a_torrent_past_its_goal_that_the_engine_would_not_remove()
        {
            _torrent.Ratio = 1.2f;
            _proxy.Setup(p => p.RemoveTorrent(Hash, false, _settings)).Throws(new InvalidOperationException("engine busy"));

            Subject.Process(GivenClient());

            Subject.OverdueTorrents.Should().Equal("Show");
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<EngineSeedingProcessedEvent>()), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_stop_listing_a_torrent_once_it_is_removed()
        {
            _torrent.Ratio = 1.2f;
            var attempts = 0;
            _proxy.Setup(p => p.RemoveTorrent(Hash, false, _settings))
                  .Callback(() =>
                  {
                      if (attempts++ == 0)
                      {
                          throw new InvalidOperationException("engine busy");
                      }
                  });

            Subject.Process(GivenClient());
            Subject.OverdueTorrents.Should().NotBeEmpty();

            Subject.Process(GivenClient());

            Subject.OverdueTorrents.Should().BeEmpty();
            ExceptionVerification.IgnoreWarns();
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
            Subject.GetRules(Hash.ToUpperInvariant(), _link).Should().Contain(r => r.Source == "tracker" && r.Mode == EngineSeedingMode.Time && r.Minutes == 3 * 24 * 60);
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
        public void should_write_only_the_ratio_of_a_single_time_or_ratio_goal_to_the_engine_once()
        {
            // The engine's seeding time starts when the torrent finished, which can be before its files were imported,
            // so a time limit there could stop it early.
            Subject.Process(GivenClient());

            _proxy.Verify(p => p.SetShareLimitsWithoutTimeLimits(Hash, 1.0, _settings), Times.Once());

            _torrent.RatioLimit = 1.0f;
            _torrent.SeedingTimeLimit = -1;
            _torrent.InactiveSeedingTimeLimit = -1;
            Subject.Process(GivenClient());

            _proxy.Verify(p => p.SetShareLimitsWithoutTimeLimits(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<QBittorrentSettings>()), Times.Once());
        }

        [Test]
        public void should_hold_to_the_tracker_minimum_saved_with_a_restored_link()
        {
            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestGrab(Hash.ToUpperInvariant()))
                .Returns(new DownloadHistory { SourceTitle = "Show.S01E01.720p.HDTV-Grp" });

            GivenGlobalGoal(EngineSeedingMode.None, 0, 0);
            _link.TrackerSeedMinutes = 3 * 24 * 60;

            Subject.Process(GivenClient());

            VerifyRemoved(false);
        }

        [Test]
        public void should_start_the_seeding_time_of_videos_imported_before_goals_existed()
        {
            GivenGlobalGoal(EngineSeedingMode.Time, 60, 0);
            _link.Files[0].ImportedAt = null;

            Subject.Process(GivenClient());

            _link.Files[0].ImportedAt.Should().NotBeNull();
            Mocker.GetMock<IEngineLinkService>().Verify(s => s.Save(Hash.ToUpperInvariant(), _link), Times.Once());
            VerifyRemoved(false);
        }

        [Test]
        public void should_seed_again_a_torrent_an_earlier_limit_stopped()
        {
            _torrent.State = "stoppedUP";
            _torrent.RatioLimit = 0.2f;
            _torrent.Ratio = 0.3f;

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.StartTorrent(Hash, _settings), Times.Once());
        }

        [Test]
        public void should_count_a_ratio_the_engine_rounds_down_as_reached()
        {
            GivenGlobalGoal(EngineSeedingMode.Ratio, 0, 1.4);
            _torrent.Ratio = 1.4f;

            Subject.Process(GivenClient());

            VerifyRemoved(true);
        }

        [Test]
        public void should_use_the_saved_tracker_minimum_when_the_indexer_is_gone()
        {
            GivenGlobalGoal(EngineSeedingMode.None, 0, 0);
            _link.TrackerSeedMinutes = 3 * 24 * 60;

            // The indexer was deleted: its id is still in the grab, but it has no settings any more.
            Subject.Process(GivenClient());

            VerifyRemoved(false);
        }

        [Test]
        public void should_retry_starting_a_torrent_when_the_last_start_failed()
        {
            _torrent.State = "stoppedUP";
            _torrent.RatioLimit = 1.0f;
            _torrent.SeedingTimeLimit = -1;
            _link.ResumeSeeding = true;

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.StartTorrent(Hash, _settings), Times.Once());
            _link.ResumeSeeding.Should().BeFalse();
        }

        private void GivenSeries(EngineSeedingRule seriesRule, EngineSeedingRule seasonRule, EngineSeedingRule episodeRule)
        {
            _link.TvdbId = 77;
            _link.Files[0].Episodes = new List<EngineLinkEpisode> { new EngineLinkEpisode { SeasonNumber = 1, EpisodeNumber = 2 } };

            var series = new NzbDrone.Core.Tv.Series
            {
                Id = 9,
                SeedingRule = seriesRule,
                Seasons = new List<NzbDrone.Core.Tv.Season> { new NzbDrone.Core.Tv.Season { SeasonNumber = 1, SeedingRule = seasonRule } }
            };

            Mocker.GetMock<NzbDrone.Core.Tv.ISeriesService>().Setup(s => s.FindByTvdbId(77)).Returns(series);
            Mocker.GetMock<NzbDrone.Core.Tv.IEpisodeService>()
                .Setup(s => s.GetEpisodeBySeries(9))
                .Returns(new List<NzbDrone.Core.Tv.Episode> { new NzbDrone.Core.Tv.Episode { Id = 20, SeasonNumber = 1, EpisodeNumber = 2, SeedingRule = episodeRule } });
        }

        [TestCase(true, true, true, "episode")]
        [TestCase(true, true, false, "season")]
        [TestCase(true, false, false, "series")]
        [TestCase(false, false, false, "global")]
        public void should_use_the_most_specific_goal_set(bool series, bool season, bool episode, string source)
        {
            var rule = new EngineSeedingRule { Mode = EngineSeedingMode.Time, Minutes = 60 };
            GivenSeries(series ? rule : null, season ? rule : null, episode ? rule : null);

            Subject.GetRules(Hash.ToUpperInvariant(), _link).Should().ContainSingle().Which.Source.Should().Be(source);
        }

        [Test]
        public void should_save_the_goals_with_the_link_while_the_series_is_known()
        {
            GivenSeries(null, new EngineSeedingRule { Mode = EngineSeedingMode.Time, Minutes = 60 }, null);

            Subject.Process(GivenClient());

            _link.SeedingRules.Should().ContainSingle().Which.Minutes.Should().Be(60);
            Mocker.GetMock<IEngineLinkService>().Verify(l => l.Save(Hash.ToUpperInvariant(), _link), Times.AtLeastOnce());
        }

        [Test]
        public void should_keep_the_saved_goals_when_the_series_is_no_longer_in_the_library()
        {
            _link.TvdbId = 77;
            _link.SeedingRules = new List<EngineSeedingRule> { new EngineSeedingRule { Mode = EngineSeedingMode.Time, Minutes = 60 } };

            var rule = Subject.GetRules(Hash.ToUpperInvariant(), _link).Should().ContainSingle().Which;

            rule.Source.Should().Be("saved");
            rule.Minutes.Should().Be(60);
        }

        [Test]
        public void should_not_use_saved_goals_for_a_movie()
        {
            _link.MediaType = EngineLinkService.MovieMediaType;
            _link.SeedingRules = new List<EngineSeedingRule> { new EngineSeedingRule { Mode = EngineSeedingMode.Time, Minutes = 60 } };

            Subject.GetRules(Hash.ToUpperInvariant(), _link).Should().ContainSingle().Which.Source.Should().Be("global");
        }

        [Test]
        public void should_give_each_imported_video_its_own_goals()
        {
            GivenSeries(null, new EngineSeedingRule { Mode = EngineSeedingMode.Time, Minutes = 60 }, null);

            var rules = Subject.GetRulesByFile(Hash.ToUpperInvariant(), _link);

            rules.Keys.Should().Equal(0);
            rules[0].Should().ContainSingle().Which.Source.Should().Be("season");
        }

        [Test]
        public void should_remove_once_a_shorter_episode_goal_is_met()
        {
            // The global goal (14 days or ratio 1) isn't met, but the episode's own goal (an hour) is.
            GivenSeries(null, null, new EngineSeedingRule { Mode = EngineSeedingMode.Time, Minutes = 60 });

            Subject.Process(GivenClient());

            VerifyRemoved(true);
        }

        [Test]
        public void should_keep_goals_a_tolerance_apart_as_two_goals()
        {
            var first = new EngineSeedingRule { Mode = EngineSeedingMode.Ratio, Ratio = 1.2341 };
            var second = new EngineSeedingRule { Mode = EngineSeedingMode.Ratio, Ratio = 1.2344 };

            first.Should().NotBe(second);
        }

        [Test]
        public void should_leave_the_engine_unlimited_for_ratio_goals_only_a_tolerance_apart()
        {
            // Two episodes of one file with ratio goals of 1.2341 and 1.2344: no single limit meets both.
            GivenGlobalGoal(EngineSeedingMode.Ratio, 0, 1.2341);
            GivenSeries(null, null, new EngineSeedingRule { Mode = EngineSeedingMode.Ratio, Ratio = 1.2344 });
            _link.Files[0].Episodes.Add(new EngineLinkEpisode { SeasonNumber = 1, EpisodeNumber = 9 });

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.SetShareLimitsWithoutTimeLimits(Hash, -1, _settings), Times.Once());
        }

        [Test]
        public void should_read_a_series_episodes_once_per_torrent()
        {
            GivenSeries(null, null, null);
            _link.Files.Add(new EngineLinkFile { Index = 2, State = EngineLinkFileState.Imported, ImportedAt = DateTime.UtcNow, Episodes = new List<EngineLinkEpisode> { new EngineLinkEpisode { SeasonNumber = 1, EpisodeNumber = 3 } } });

            Subject.Process(GivenClient());

            Mocker.GetMock<NzbDrone.Core.Tv.IEpisodeService>().Verify(s => s.GetEpisodeBySeries(9), Times.Once());
            Mocker.GetMock<NzbDrone.Core.Tv.IEpisodeService>().Verify(s => s.FindEpisode(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_keep_seeding_with_a_goal_mode_it_does_not_know()
        {
            GivenGlobalGoal((EngineSeedingMode)5, 0, 0);

            Subject.Process(GivenClient());

            VerifyRemoved(false);
        }

        [Test]
        public void should_leave_a_download_the_user_ignored_alone()
        {
            GivenGlobalGoal(EngineSeedingMode.None, 0, 0);

            Mocker.GetMock<NzbDrone.Core.Download.TrackedDownloads.ITrackedDownloadService>()
                .Setup(s => s.Find(Hash.ToUpperInvariant()))
                .Returns(new NzbDrone.Core.Download.TrackedDownloads.TrackedDownload { State = NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadState.Ignored });

            Subject.Process(GivenClient());

            VerifyRemoved(false);
            _proxy.Verify(p => p.SetShareLimitsWithoutTimeLimits(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_seed_again_a_torrent_an_earlier_inactive_time_limit_stopped()
        {
            _torrent.State = "stoppedUP";
            _torrent.InactiveSeedingTimeLimit = 60;

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.SetShareLimitsWithoutTimeLimits(Hash, 1.0, _settings), Times.Once());
            _proxy.Verify(p => p.StartTorrent(Hash, _settings), Times.Once());
        }

        [Test]
        public void should_leave_a_torrent_stopped_by_hand_stopped()
        {
            _torrent.State = "stoppedUP";
            _torrent.RatioLimit = 2.0f;
            _torrent.Ratio = 0.3f;

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.StartTorrent(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_leave_the_engine_unlimited_when_its_limits_would_stop_seeding_too_early()
        {
            // The engine stops at whichever limit comes first, which can't express a goal plus a tracker's minimum.
            _trackerMinimum = new TorrentSeedConfiguration { SeedTime = TimeSpan.FromDays(3) };

            Subject.Process(GivenClient());

            _proxy.Verify(p => p.SetShareLimitsWithoutTimeLimits(Hash, -1, _settings), Times.Once());
        }
    }
}
