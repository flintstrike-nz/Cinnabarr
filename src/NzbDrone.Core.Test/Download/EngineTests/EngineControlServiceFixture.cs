using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineControlServiceFixture : CoreTest<EngineControlService>
    {
        private const string Ready = "abcdef0123456789abcdef0123456789abcdef01";
        private const string Held = "1234567890abcdef1234567890abcdef12345678";

        private QBittorrentSettings _settings;
        private Mock<IQBittorrentProxy> _proxy;
        private bool _altSpeed;

        [SetUp]
        public void Setup()
        {
            _settings = new QBittorrentSettings();
            _proxy = new Mock<IQBittorrentProxy>();
            _altSpeed = false;

            var client = new Mock<IDownloadClient>();
            client.SetupGet(c => c.Definition).Returns(new DownloadClientDefinition { Id = 3, Settings = _settings });

            Mocker.GetMock<IProvideDownloadClient>().Setup(s => s.GetDownloadClients(false)).Returns(new[] { client.Object });
            Mocker.GetMock<IEngineClientRegistry>().Setup(s => s.IsEngineClient(3)).Returns(true);
            Mocker.GetMock<IQBittorrentProxySelector>().Setup(s => s.GetProxy(_settings, false)).Returns(_proxy.Object);

            _proxy.Setup(p => p.GetTorrents(_settings)).Returns(new List<QBittorrentTorrent>
            {
                new QBittorrentTorrent { Hash = Ready, Tags = $"{EngineTorrentTags.Held},{EngineTorrentTags.Prepared}" },
                new QBittorrentTorrent { Hash = Held, Tags = EngineTorrentTags.Held }
            });
            _proxy.Setup(p => p.GetAlternativeSpeedLimitsEnabled(_settings)).Returns(() => _altSpeed);
            _proxy.Setup(p => p.GetTransferInfo(_settings)).Returns(new QBittorrentTransferInfo { DownloadSpeed = 1000, UploadSpeed = 200 });
        }

        [Test]
        public void should_read_the_engine_preferences_in_kib_per_second()
        {
            _proxy.Setup(p => p.GetPreferencesJson(_settings)).Returns(JObject.Parse(
                "{\"listen_port\": 6881, \"upnp\": true, \"dl_limit\": 2048000, \"up_limit\": 0, \"max_active_downloads\": 3, \"anonymous_mode\": true, \"preallocate_all\": true, \"current_network_interface\": \"eth0\"}"));

            var preferences = Subject.GetPreferences();

            preferences.ListenPort.Should().Be(6881);
            preferences.Upnp.Should().BeTrue();
            preferences.DownloadLimit.Should().Be(2000);
            preferences.UploadLimit.Should().Be(0);
            preferences.MaxActiveDownloads.Should().Be(3);
            preferences.AnonymousMode.Should().BeTrue();
            preferences.PreallocateDisk.Should().BeTrue();
            preferences.NetworkInterface.Should().Be("eth0");
        }

        [Test]
        public void should_write_the_engine_preferences_in_bytes_per_second()
        {
            JObject sent = null;
            _proxy.Setup(p => p.SetPreferencesJson(It.IsAny<JObject>(), _settings)).Callback<JObject, QBittorrentSettings>((json, _) => sent = json);

            Subject.SetPreferences(new EnginePreferences { ListenPort = 51413, DownloadLimit = 500, UploadLimit = -5, MaxActiveTorrents = 8, Dht = true });

            sent.Value<int>("listen_port").Should().Be(51413);
            sent.Value<int>("dl_limit").Should().Be(500 * 1024);
            sent.Value<int>("up_limit").Should().Be(0);
            sent.Value<int>("max_active_torrents").Should().Be(8);
            sent.Value<bool>("dht").Should().BeTrue();
        }

        [Test]
        public void should_read_and_write_the_alternative_speed_schedule()
        {
            _proxy.Setup(p => p.GetPreferencesJson(_settings)).Returns(JObject.Parse(
                "{\"scheduler_enabled\": true, \"schedule_from_hour\": 8, \"schedule_from_min\": 30, \"schedule_to_hour\": 22, \"schedule_to_min\": 5, \"scheduler_days\": 1}"));

            var preferences = Subject.GetPreferences();

            preferences.SchedulerEnabled.Should().BeTrue();
            preferences.ScheduleFromHour.Should().Be(8);
            preferences.ScheduleFromMinute.Should().Be(30);
            preferences.ScheduleToHour.Should().Be(22);
            preferences.ScheduleToMinute.Should().Be(5);
            preferences.SchedulerDays.Should().Be(1);

            JObject sent = null;
            _proxy.Setup(p => p.SetPreferencesJson(It.IsAny<JObject>(), _settings)).Callback<JObject, QBittorrentSettings>((json, _) => sent = json);

            Subject.SetPreferences(preferences);

            sent.Value<bool>("scheduler_enabled").Should().BeTrue();
            sent.Value<int>("schedule_from_hour").Should().Be(8);
            sent.Value<int>("schedule_to_min").Should().Be(5);
            sent.Value<int>("scheduler_days").Should().Be(1);
        }

        [Test]
        public void should_refuse_schedule_times_and_days_that_are_not_real()
        {
            Assert.Throws<EngineControlException>(() => Subject.SetPreferences(new EnginePreferences { ScheduleFromHour = 24 }));
            Assert.Throws<EngineControlException>(() => Subject.SetPreferences(new EnginePreferences { ScheduleToMinute = 60 }));
            Assert.Throws<EngineControlException>(() => Subject.SetPreferences(new EnginePreferences { SchedulerDays = 10 }));

            _proxy.Verify(p => p.SetPreferencesJson(It.IsAny<JObject>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_refuse_a_listening_port_out_of_range()
        {
            Assert.Throws<EngineControlException>(() => Subject.SetPreferences(new EnginePreferences { ListenPort = 70000 }));

            _proxy.Verify(p => p.SetPreferencesJson(It.IsAny<JObject>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_report_speeds_and_the_alternative_speed_limits()
        {
            _altSpeed = true;

            var status = Subject.GetStatus();

            status.DownloadSpeed.Should().Be(1000);
            status.UploadSpeed.Should().Be(200);
            status.AlternativeSpeedLimits.Should().BeTrue();
        }

        [TestCase(false, true, 1)]
        [TestCase(true, true, 0)]
        [TestCase(true, false, 1)]
        public void should_only_toggle_the_alternative_speed_limits_when_they_differ(bool current, bool wanted, int toggles)
        {
            _altSpeed = current;

            Subject.SetAlternativeSpeedLimits(wanted);

            _proxy.Verify(p => p.ToggleAlternativeSpeedLimits(_settings), Times.Exactly(toggles));
        }

        [Test]
        public void should_resume_all_but_torrents_still_being_prepared()
        {
            Subject.ResumeAll();

            _proxy.Verify(p => p.StartTorrents(Ready, _settings), Times.Once());
        }

        [Test]
        public void should_not_start_a_torrent_still_being_prepared()
        {
            Assert.Throws<EngineControlException>(() => Subject.Resume(Held.ToUpperInvariant()));
            Assert.Throws<EngineControlException>(() => Subject.SetForceStart(Held, true));

            _proxy.Verify(p => p.StartTorrents(It.IsAny<string>(), _settings), Times.Never());
            _proxy.Verify(p => p.SetForceStart(It.IsAny<string>(), It.IsAny<bool>(), _settings), Times.Never());
        }

        [Test]
        public void should_pause_recheck_and_stop_forcing_any_torrent()
        {
            Subject.Pause(Held.ToUpperInvariant());
            Subject.Recheck(Ready);
            Subject.SetForceStart(Held, false);

            _proxy.Verify(p => p.StopTorrents(Held, _settings), Times.Once());
            _proxy.Verify(p => p.RecheckTorrent(Ready, _settings), Times.Once());
            _proxy.Verify(p => p.SetForceStart(Held, false, _settings), Times.Once());
        }

        [Test]
        public void should_move_a_download_in_the_queue_when_queueing_is_on()
        {
            _proxy.Setup(p => p.GetPreferencesJson(_settings)).Returns(JObject.Parse("{\"queueing_enabled\": true}"));

            Subject.MoveInQueue(Ready, "top");

            _proxy.Verify(p => p.MoveTorrentInQueue(Ready, "top", _settings), Times.Once());
        }

        [Test]
        public void should_refuse_a_queue_move_when_queueing_is_off_or_the_move_is_unknown()
        {
            _proxy.Setup(p => p.GetPreferencesJson(_settings)).Returns(JObject.Parse("{\"queueing_enabled\": false}"));

            Assert.Throws<EngineControlException>(() => Subject.MoveInQueue(Ready, "top"));
            Assert.Throws<EngineControlException>(() => Subject.MoveInQueue(Ready, "sideways"));

            _proxy.Verify(p => p.MoveTorrentInQueue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_set_a_downloads_own_speed_limits_in_bytes()
        {
            Subject.SetSpeedLimits(Ready, 500, 0);

            _proxy.Verify(p => p.SetTorrentSpeedLimits(Ready, 500 * 1024L, 0, _settings), Times.Once());
            Assert.Throws<EngineControlException>(() => Subject.SetSpeedLimits(Ready, -1, 0));
        }

        [Test]
        public void should_refuse_a_download_the_engine_does_not_have()
        {
            Assert.Throws<EngineControlException>(() => Subject.Pause(new string('f', 40)));
        }

        [Test]
        public void should_report_an_engine_that_cannot_be_reached()
        {
            _proxy.Setup(p => p.GetTransferInfo(_settings)).Throws(new InvalidOperationException("down"));

            Assert.Throws<EngineControlException>(() => Subject.GetStatus());
        }

        [Test]
        public void should_add_a_magnet_by_hand_while_holding_the_add_gate()
        {
            var magnet = "magnet:?xt=urn:btih:" + new string('c', 40) + "&dn=Show";
            var gate = new Mock<IDisposable>();
            Mocker.GetMock<IEngineAddGate>().Setup(s => s.BeginAdd()).Returns(gate.Object);

            Subject.AddMagnet(magnet).Should().Be(new string('C', 40));

            _proxy.Verify(p => p.AddManualTorrentFromUrl(magnet, _settings), Times.Once());
            gate.Verify(g => g.Dispose(), Times.Once());
        }

        [Test]
        public void should_refuse_an_invalid_magnet_or_one_the_engine_already_has()
        {
            Assert.Throws<EngineControlException>(() => Subject.AddMagnet("not a magnet"));
            Assert.Throws<EngineControlException>(() => Subject.AddMagnet("magnet:?xt=urn:btih:" + Ready));

            _proxy.Verify(p => p.AddManualTorrentFromUrl(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void should_add_a_torrent_file_by_hand()
        {
            var content = new byte[] { 1, 2, 3 };
            Mocker.GetMock<ITorrentFileInfoReader>().Setup(s => s.GetHashFromTorrentFile(content)).Returns(new string('d', 40));

            Subject.AddTorrentFile("show.torrent", content).Should().Be(new string('D', 40));

            _proxy.Verify(p => p.AddManualTorrentFromFile("show.torrent", content, _settings), Times.Once());
        }

        [Test]
        public void should_refuse_an_invalid_torrent_file()
        {
            Mocker.GetMock<ITorrentFileInfoReader>().Setup(s => s.GetHashFromTorrentFile(It.IsAny<byte[]>())).Throws(new FormatException("bad"));

            Assert.Throws<EngineControlException>(() => Subject.AddTorrentFile("x.torrent", new byte[] { 1 }));
        }
    }
}
