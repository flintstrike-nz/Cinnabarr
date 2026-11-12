using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Versions;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class SeriesVersionNamingFixture : CoreTest<FileNameBuilder>
    {
        private Series _series;
        private Episode _episode;
        private EpisodeFile _file;
        private NamingConfig _config;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew().With(s => s.Title = "Chernobyl").Build();
            _episode = Builder<Episode>.CreateNew().With(e => e.Title = "Please Remain Calm").With(e => e.SeasonNumber = 1).With(e => e.EpisodeNumber = 2).Build();
            _file = new EpisodeFile { Quality = new QualityModel(Quality.Bluray2160p), RelativePath = "x.mkv", SceneName = "Chernobyl.S01E02.2160p" };

            _config = NamingConfig.Default;
            _config.RenameEpisodes = true;

            Mocker.GetMock<INamingConfigService>().Setup(c => c.GetConfig()).Returns(_config);
            Mocker.GetMock<IQualityDefinitionService>().Setup(s => s.Get(It.IsAny<Quality>())).Returns<Quality>(q => new QualityDefinition(q) { Title = q.Name });
        }

        private Series ViewOf(string label)
        {
            var view = _series.ShallowCopy();

            view.Slot = new SeriesVersionSlot { Id = 3, SeriesId = _series.Id, Label = label };

            return view;
        }

        [Test]
        public void should_add_the_label_of_a_version_to_the_end_of_the_name()
        {
            var plain = Subject.BuildFileName(new List<Episode> { _episode }, _series, _file);
            var version = Subject.BuildFileName(new List<Episode> { _episode }, ViewOf("4K"), _file);

            plain.Should().NotEndWith("4K");
            version.Should().Be(plain + " - 4K");
        }

        [Test]
        public void should_place_the_label_where_a_version_token_says()
        {
            _config.StandardEpisodeFormat = "{Series Title} - S{season:00}E{episode:00} [{Version}]";

            Subject.BuildFileName(new List<Episode> { _episode }, ViewOf("4K"), _file).Should().Be("Chernobyl - S01E02 [4K]");
            Subject.BuildFileName(new List<Episode> { _episode }, _series, _file).Should().Be("Chernobyl - S01E02 []");
        }

        [Test]
        public void should_keep_the_label_when_files_are_not_renamed()
        {
            _config.RenameEpisodes = false;

            Subject.BuildFileName(new List<Episode> { _episode }, ViewOf("4K"), _file).Should().Be("Chernobyl.S01E02.2160p - 4K");
            Subject.BuildFileName(new List<Episode> { _episode }, _series, _file).Should().Be("Chernobyl.S01E02.2160p");
        }
    }

    [TestFixture]
    public class SeriesVersionHistoryFixture : CoreTest<HistoryService>
    {
        private List<EpisodeHistory> _inserted;

        [SetUp]
        public void Setup()
        {
            _inserted = new List<EpisodeHistory>();
            Mocker.GetMock<IHistoryRepository>().Setup(r => r.Insert(It.IsAny<EpisodeHistory>())).Callback<EpisodeHistory>(_inserted.Add);
        }

        private static RemoteEpisode Remote(Series series)
        {
            return new RemoteEpisode
            {
                Series = series,
                Episodes = new List<Episode> { new Episode { Id = 10, SeriesId = series.Id } },
                Release = new ReleaseInfo { Title = "Chernobyl.S01E02.2160p", Guid = "g" },
                ParsedEpisodeInfo = new ParsedEpisodeInfo { Quality = new QualityModel(Quality.Bluray2160p) },
                Languages = new List<NzbDrone.Core.Languages.Language>()
            };
        }

        [Test]
        public void should_record_the_version_a_release_was_grabbed_for()
        {
            var series = new Series { Id = 5, Slot = new SeriesVersionSlot { Id = 3, Label = "4K" } };

            Subject.Handle(new EpisodeGrabbedEvent(Remote(series)) { DownloadId = "ABC" });

            _inserted.Single().VersionSlotId.Should().Be(3);
            _inserted.Single().Data[EpisodeHistory.VERSION_SLOT_ID].Should().Be("3");
        }

        [Test]
        public void should_not_record_a_version_for_the_default_version()
        {
            Subject.Handle(new EpisodeGrabbedEvent(Remote(new Series { Id = 5 })) { DownloadId = "ABC" });

            _inserted.Single().VersionSlotId.Should().Be(0);
            _inserted.Single().Data.Should().NotContainKey(EpisodeHistory.VERSION_SLOT_ID);
        }

        [Test]
        public void should_find_the_latest_event_of_one_version_only()
        {
            var history = new List<EpisodeHistory>
            {
                new EpisodeHistory { Id = 3, Data = new Dictionary<string, string> { [EpisodeHistory.VERSION_SLOT_ID] = "3" } },
                new EpisodeHistory { Id = 2 },
                new EpisodeHistory { Id = 1, Data = new Dictionary<string, string> { [EpisodeHistory.VERSION_SLOT_ID] = "4" } }
            };

            Mocker.GetMock<IHistoryRepository>().Setup(r => r.FindByEpisodeId(10)).Returns(history);

            Subject.MostRecentForEpisode(10, 3).Id.Should().Be(3);
            Subject.MostRecentForEpisode(10, 0).Id.Should().Be(2);
            Subject.MostRecentForEpisode(10, 4).Id.Should().Be(1);
            Subject.MostRecentForEpisode(10, 9).Should().BeNull();
        }
    }

    [TestFixture]
    public class SeriesVersionQueueSpecificationFixture : CoreTest<QueueSpecification>
    {
        private static QualityProfile Profile()
        {
            return new QualityProfile
            {
                UpgradeAllowed = true,
                Cutoff = Quality.HDTV720p.Id,
                Items = NzbDrone.Core.Test.Qualities.QualityFixture.GetDefaultQualities(Quality.SDTV, Quality.HDTV720p, Quality.Bluray2160p),
                FormatItems = new List<NzbDrone.Core.Profiles.ProfileFormatItem>()
            };
        }

        private static Series View(int slotId)
        {
            return new Series { Id = 5, Slot = slotId == 0 ? null : new SeriesVersionSlot { Id = slotId, Label = "V" + slotId }, QualityProfile = Profile() };
        }

        private static RemoteEpisode Remote(Series series)
        {
            return new RemoteEpisode
            {
                Series = series,
                Episodes = new List<Episode> { new Episode { Id = 10, SeriesId = 5 } },
                ParsedEpisodeInfo = new ParsedEpisodeInfo { Quality = new QualityModel(Quality.HDTV720p) },
                Release = new ReleaseInfo(),
                CustomFormats = new List<CustomFormat>()
            };
        }

        private void GivenQueued(Series series)
        {
            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(s => s.ParseCustomFormat(It.IsAny<RemoteEpisode>(), It.IsAny<long>()))
                  .Returns(new List<CustomFormat>());

            Mocker.GetMock<IQueueService>().Setup(q => q.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue>
            {
                new NzbDrone.Core.Queue.Queue { Id = 1, RemoteEpisode = Remote(series), Size = 100, TrackedDownloadState = TrackedDownloadState.Downloading }
            });
        }

        [Test]
        public void a_download_for_one_version_does_not_block_another_version_of_the_same_episode()
        {
            GivenQueued(View(3));

            Subject.IsSatisfiedBy(Remote(View(0)), new ReleaseDecisionInformation(false, null)).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(Remote(View(4)), new ReleaseDecisionInformation(false, null)).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_download_for_a_version_blocks_a_release_for_the_same_version()
        {
            GivenQueued(View(3));

            Subject.IsSatisfiedBy(Remote(View(3)), new ReleaseDecisionInformation(false, null)).Accepted.Should().BeFalse();
        }
    }
}
