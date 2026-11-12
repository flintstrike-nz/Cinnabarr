using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;
using NzbDrone.Core.Tv.Versions;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class DefaultSeriesVersionServiceFixture : CoreTest<DefaultSeriesVersionService>
    {
        private string _stored;

        [SetUp]
        public void Setup()
        {
            _stored = string.Empty;

            Mocker.GetMock<IConfigService>().SetupGet(c => c.DefaultSeriesVersions).Returns(() => _stored);
            Mocker.GetMock<IConfigService>().SetupSet(c => c.DefaultSeriesVersions = It.IsAny<string>()).Callback<string>(v => _stored = v);
            Mocker.GetMock<IQualityProfileService>().Setup(q => q.Exists(It.IsAny<int>())).Returns(true);
        }

        [Test]
        public void should_have_no_default_versions_until_some_are_set()
        {
            Subject.Get().Should().BeEmpty();
        }

        [Test]
        public void should_store_and_read_back_the_default_versions()
        {
            Subject.Set(new List<DefaultVersion> { new DefaultVersion { Label = " 4K ", QualityProfileId = 3 } });

            var versions = Subject.Get();

            versions.Should().ContainSingle();
            versions[0].Label.Should().Be("4K");
            versions[0].QualityProfileId.Should().Be(3);
        }

        [Test]
        public void should_refuse_an_empty_label_a_repeated_label_or_an_unknown_profile()
        {
            Assert.Throws<NzbDroneClientException>(() => Subject.Set(new List<DefaultVersion> { new DefaultVersion { Label = " ", QualityProfileId = 1 } }));
            Assert.Throws<NzbDroneClientException>(() => Subject.Set(new List<DefaultVersion> { new DefaultVersion { Label = "4K", QualityProfileId = 1 }, new DefaultVersion { Label = "4k", QualityProfileId = 2 } }));

            Mocker.GetMock<IQualityProfileService>().Setup(q => q.Exists(9)).Returns(false);

            Assert.Throws<NzbDroneClientException>(() => Subject.Set(new List<DefaultVersion> { new DefaultVersion { Label = "4K", QualityProfileId = 9 } }));
        }

        [Test]
        public void should_give_a_new_series_each_default_version()
        {
            Subject.Set(new List<DefaultVersion> { new DefaultVersion { Label = "4K", QualityProfileId = 3 }, new DefaultVersion { Label = "HD", QualityProfileId = 2 } });

            Subject.Handle(new SeriesAddedEvent(new Series { Id = 5, Monitored = true }));

            Mocker.GetMock<ISeriesVersionSlotService>().Verify(s => s.Add(It.Is<SeriesVersionSlot>(v => v.SeriesId == 5 && v.Label == "4K" && v.QualityProfileId == 3 && v.Monitored)), Times.Once());
            Mocker.GetMock<ISeriesVersionSlotService>().Verify(s => s.Add(It.Is<SeriesVersionSlot>(v => v.SeriesId == 5 && v.Label == "HD" && v.QualityProfileId == 2)), Times.Once());
        }

        [Test]
        public void should_give_each_imported_series_the_default_versions_and_survive_a_failure()
        {
            Subject.Set(new List<DefaultVersion> { new DefaultVersion { Label = "4K", QualityProfileId = 3 } });

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(It.IsAny<IEnumerable<int>>())).Returns(new List<Series> { new Series { Id = 1 }, new Series { Id = 2 } });
            Mocker.GetMock<ISeriesVersionSlotService>().Setup(s => s.Add(It.Is<SeriesVersionSlot>(v => v.SeriesId == 1))).Throws(new NzbDroneClientException(System.Net.HttpStatusCode.BadRequest, "no"));

            Subject.Handle(new SeriesImportedEvent(new List<int> { 1, 2 }));

            Mocker.GetMock<ISeriesVersionSlotService>().Verify(s => s.Add(It.Is<SeriesVersionSlot>(v => v.SeriesId == 2)), Times.Once());
            ExceptionVerification.IgnoreWarns();
        }
    }
}
