using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Download.Engine;
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
    }
}
