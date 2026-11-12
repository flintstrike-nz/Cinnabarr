using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;
using NzbDrone.Core.Tv.Versions;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class SeriesVersionSlotServiceFixture : CoreTest<SeriesVersionSlotService>
    {
        private Series _series;
        private List<Episode> _episodes;
        private List<EpisodeVersionFile> _links;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Id = 5, Monitored = true, QualityProfileId = 1 };
            _episodes = new List<Episode> { new Episode { Id = 10, SeriesId = 5, EpisodeFileId = 70 }, new Episode { Id = 11, SeriesId = 5 } };
            _links = new List<EpisodeVersionFile>();

            Mocker.GetMock<IQualityProfileService>().Setup(q => q.Exists(It.IsAny<int>())).Returns(true);
            Mocker.GetMock<IQualityProfileService>().Setup(q => q.Get(2)).Returns(new QualityProfile { Id = 2, Name = "UHD" });
            Mocker.GetMock<ISeriesVersionSlotRepository>().Setup(r => r.GetBySeries(5)).Returns(() => new List<SeriesVersionSlot>());
            Mocker.GetMock<IEpisodeVersionFileRepository>().Setup(r => r.GetByEpisodes(It.IsAny<List<int>>())).Returns((List<int> ids) => _links.Where(l => ids.Contains(l.EpisodeId)).ToList());
            Mocker.GetMock<IEpisodeVersionFileRepository>().Setup(r => r.Insert(It.IsAny<EpisodeVersionFile>())).Returns(AddLink);
        }

        private EpisodeVersionFile AddLink(EpisodeVersionFile link)
        {
            _links.Add(link);

            return link;
        }

        [Test]
        public void should_add_a_version_with_a_trimmed_label()
        {
            Mocker.GetMock<ISeriesVersionSlotRepository>().Setup(r => r.Insert(It.IsAny<SeriesVersionSlot>())).Returns((SeriesVersionSlot s) => s);

            var slot = Subject.Add(new SeriesVersionSlot { SeriesId = 5, Label = " 4K ", QualityProfileId = 2, Monitored = true });

            slot.Label.Should().Be("4K");
            slot.Added.Should().NotBe(default);
        }

        [Test]
        public void should_refuse_an_empty_label_a_bad_character_an_unknown_profile_and_a_repeated_label()
        {
            Assert.Throws<NzbDroneClientException>(() => Subject.Add(new SeriesVersionSlot { SeriesId = 5, Label = " ", QualityProfileId = 2 }));
            Assert.Throws<NzbDroneClientException>(() => Subject.Add(new SeriesVersionSlot { SeriesId = 5, Label = "4K/HDR", QualityProfileId = 2 }));

            Mocker.GetMock<IQualityProfileService>().Setup(q => q.Exists(9)).Returns(false);
            Assert.Throws<NzbDroneClientException>(() => Subject.Add(new SeriesVersionSlot { SeriesId = 5, Label = "4K", QualityProfileId = 9 }));

            Mocker.GetMock<ISeriesVersionSlotRepository>().Setup(r => r.GetBySeries(5)).Returns(new List<SeriesVersionSlot> { new SeriesVersionSlot { Id = 1, SeriesId = 5, Label = "4k" } });
            Assert.Throws<NzbDroneClientException>(() => Subject.Add(new SeriesVersionSlot { SeriesId = 5, Label = "4K", QualityProfileId = 2 }));
        }

        [Test]
        public void should_refuse_more_than_the_limit_of_versions()
        {
            Mocker.GetMock<ISeriesVersionSlotRepository>().Setup(r => r.GetBySeries(5)).Returns(Enumerable.Range(1, SeriesVersionSlotService.MaxSlotsPerSeries - 1).Select(i => new SeriesVersionSlot { Id = i, SeriesId = 5, Label = "V" + i }).ToList());

            Assert.Throws<NzbDroneClientException>(() => Subject.Add(new SeriesVersionSlot { SeriesId = 5, Label = "One more", QualityProfileId = 2 }));
        }

        [Test]
        public void should_make_a_view_with_the_slots_profile_and_monitored_state_and_leave_the_default_alone()
        {
            Mocker.GetMock<ISeriesVersionSlotRepository>().Setup(r => r.Get(7)).Returns(new SeriesVersionSlot { Id = 7, SeriesId = 5, Label = "UHD", QualityProfileId = 2, Monitored = false });

            Subject.GetView(_series, 0).Should().BeSameAs(_series);

            var view = Subject.GetView(_series, 7);

            view.Id.Should().Be(5);
            view.QualityProfileId.Should().Be(2);
            view.Monitored.Should().BeFalse();
            view.VersionSlotId.Should().Be(7);
            view.VersionLabel.Should().Be("UHD");
            _series.QualityProfileId.Should().Be(1);
            _series.Monitored.Should().BeTrue();
        }

        [Test]
        public void should_refuse_a_view_of_another_seriess_version()
        {
            Mocker.GetMock<ISeriesVersionSlotRepository>().Setup(r => r.Get(7)).Returns(new SeriesVersionSlot { Id = 7, SeriesId = 99 });

            Assert.Throws<NzbDroneClientException>(() => Subject.GetView(_series, 7));
        }

        [Test]
        public void should_show_each_episode_with_the_slots_file_or_none()
        {
            _links.Add(new EpisodeVersionFile { EpisodeId = 11, VersionSlotId = 7, EpisodeFileId = 80 });
            _links.Add(new EpisodeVersionFile { EpisodeId = 10, VersionSlotId = 8, EpisodeFileId = 81 });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<EpisodeFile> { new EpisodeFile { Id = 80 } });

            var views = Subject.GetEpisodeViews(_episodes, 7);

            views[0].EpisodeFileId.Should().Be(0);
            views[0].HasFile.Should().BeFalse();
            views[1].EpisodeFileId.Should().Be(80);
            _episodes[0].EpisodeFileId.Should().Be(70);
            Subject.GetEpisodeViews(_episodes, 0).Should().BeSameAs(_episodes);
        }

        [Test]
        public void should_record_a_slot_file_for_its_episodes_replacing_an_older_one_and_ignore_an_own_file()
        {
            _links.Add(new EpisodeVersionFile { Id = 1, EpisodeId = 10, VersionSlotId = 7, EpisodeFileId = 60 });

            var file = new EpisodeFile { Id = 90, VersionSlotId = 7, Episodes = new LazyLoaded<List<Episode>>(new List<Episode> { _episodes[0] }) };

            Subject.Handle(new EpisodeFileAddedEvent(file));
            Subject.Handle(new EpisodeFileAddedEvent(new EpisodeFile { Id = 91, VersionSlotId = 0, Episodes = new LazyLoaded<List<Episode>>(_episodes) }));

            Mocker.GetMock<IEpisodeVersionFileRepository>().Verify(r => r.Delete(It.Is<EpisodeVersionFile>(l => l.Id == 1)), Times.Once());
            _links.Should().Contain(l => l.EpisodeId == 10 && l.EpisodeFileId == 90 && l.VersionSlotId == 7);
            _links.Should().NotContain(l => l.EpisodeFileId == 91);
        }

        [Test]
        public void should_forget_the_links_of_a_deleted_slot_file_and_of_a_deleted_series()
        {
            Subject.Handle(new EpisodeFileDeletedEvent(new EpisodeFile { Id = 90, VersionSlotId = 7 }, DeleteMediaFileReason.Manual));
            Subject.Handle(new EpisodeFileDeletedEvent(new EpisodeFile { Id = 91, VersionSlotId = 0 }, DeleteMediaFileReason.Manual));

            Mocker.GetMock<IEpisodeVersionFileRepository>().Verify(r => r.DeleteForFile(90), Times.Once());
            Mocker.GetMock<IEpisodeVersionFileRepository>().Verify(r => r.DeleteForFile(91), Times.Never());

            Mocker.GetMock<ISeriesVersionSlotRepository>().Setup(r => r.GetBySeries(5)).Returns(new List<SeriesVersionSlot> { new SeriesVersionSlot { Id = 3, SeriesId = 5 } });
            Subject.HandleAsync(new SeriesDeletedEvent(new List<Series> { _series }, false, false));

            Mocker.GetMock<IEpisodeVersionFileRepository>().Verify(r => r.DeleteForSlots(It.Is<List<int>>(l => l.SequenceEqual(new[] { 3 }))), Times.Once());
            Mocker.GetMock<ISeriesVersionSlotRepository>().Verify(r => r.DeleteForSeries(It.Is<List<int>>(l => l.SequenceEqual(new[] { 5 }))), Times.Once());
        }

        [Test]
        public void should_not_delete_a_version_that_still_has_files()
        {
            Mocker.GetMock<ISeriesVersionSlotRepository>().Setup(r => r.Get(7)).Returns(new SeriesVersionSlot { Id = 7, SeriesId = 5 });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.GetFilesBySeries(5)).Returns(new List<EpisodeFile> { new EpisodeFile { VersionSlotId = 7 } });

            Assert.Throws<NzbDroneClientException>(() => Subject.Delete(7));

            Mocker.GetMock<IMediaFileService>().Setup(m => m.GetFilesBySeries(5)).Returns(new List<EpisodeFile>());
            Subject.Delete(7);

            Mocker.GetMock<ISeriesVersionSlotRepository>().Verify(r => r.Delete(7), Times.Once());
        }

        private void GivenWantedSetup()
        {
            _series.Monitored = true;
            _episodes[0].Monitored = true;
            _episodes[0].AirDateUtc = DateTime.UtcNow.AddDays(-10);
            _episodes[1].Monitored = true;
            _episodes[1].AirDateUtc = DateTime.UtcNow.AddDays(-10);

            Mocker.GetMock<ISeriesVersionSlotRepository>().Setup(r => r.All()).Returns(new List<SeriesVersionSlot>
            {
                new SeriesVersionSlot { Id = 7, SeriesId = 5, Label = "UHD", QualityProfileId = 2, Monitored = true },
                new SeriesVersionSlot { Id = 8, SeriesId = 5, Label = "Off", QualityProfileId = 2, Monitored = false },
                new SeriesVersionSlot { Id = 9, SeriesId = 6, Label = "UHD", QualityProfileId = 2, Monitored = true }
            });

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(It.IsAny<IEnumerable<int>>())).Returns(new List<Series> { _series, new Series { Id = 6, Monitored = false } });
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(5)).Returns(_episodes);
        }

        [Test]
        public void should_list_the_episodes_a_monitored_version_of_a_monitored_series_is_missing()
        {
            GivenWantedSetup();

            // The default file of episode 10 doesn't count for the version; episode 11 has the version's file.
            _links.Add(new EpisodeVersionFile { EpisodeId = 11, VersionSlotId = 7, EpisodeFileId = 80 });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<EpisodeFile> { new EpisodeFile { Id = 80 } });

            var missing = Subject.GetMissing();

            // Not the unmonitored version, and not the version of the unmonitored series.
            var wanted = missing.Should().ContainSingle().Subject;

            wanted.Series.VersionSlotId.Should().Be(7);
            wanted.Series.Id.Should().Be(5);
            wanted.Episodes.Select(e => e.Id).Should().Equal(10);
            wanted.Episodes[0].HasFile.Should().BeFalse();
        }

        [Test]
        public void should_not_list_an_episode_that_has_not_aired_or_is_not_monitored()
        {
            GivenWantedSetup();
            _episodes[0].AirDateUtc = DateTime.UtcNow.AddDays(3);
            _episodes[1].Monitored = false;

            Subject.GetMissing().Should().BeEmpty();
        }

        [Test]
        public void should_limit_the_wanted_versions_to_the_series_asked_for()
        {
            GivenWantedSetup();

            Subject.GetMissing(new List<int> { 6 }).Should().BeEmpty();
            Subject.GetMissing(new List<int> { 5 }).Should().ContainSingle();
        }

        [Test]
        public void should_list_the_episodes_whose_file_for_the_version_is_below_the_cutoff()
        {
            GivenWantedSetup();

            Mocker.GetMock<IQualityProfileService>().Setup(q => q.Get(2)).Returns(new QualityProfile
            {
                Id = 2,
                UpgradeAllowed = true,
                Cutoff = Quality.Bluray2160p.Id,
                Items = NzbDrone.Core.Test.Qualities.QualityFixture.GetDefaultQualities(Quality.SDTV, Quality.HDTV720p, Quality.Bluray2160p)
            });

            _links.Add(new EpisodeVersionFile { EpisodeId = 10, VersionSlotId = 7, EpisodeFileId = 80 });
            _links.Add(new EpisodeVersionFile { EpisodeId = 11, VersionSlotId = 7, EpisodeFileId = 81 });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<EpisodeFile>
            {
                new EpisodeFile { Id = 80, Quality = new QualityModel(Quality.HDTV720p) },
                new EpisodeFile { Id = 81, Quality = new QualityModel(Quality.Bluray2160p) }
            });

            var wanted = Subject.GetCutoffUnmet().Should().ContainSingle().Subject;

            wanted.Episodes.Select(e => e.Id).Should().Equal(10);
            wanted.Episodes[0].EpisodeFileId.Should().Be(80);
        }
    }
}
