using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class DefaultVersionServiceFixture : CoreTest<DefaultVersionService>
    {
        private string _stored;

        [SetUp]
        public void Setup()
        {
            _stored = string.Empty;

            Mocker.GetMock<IConfigService>().SetupGet(c => c.DefaultMovieVersions).Returns(() => _stored);
            Mocker.GetMock<IConfigService>().SetupSet(c => c.DefaultMovieVersions = It.IsAny<string>()).Callback<string>(v => _stored = v);
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
        public void should_give_a_new_movie_each_default_version()
        {
            Subject.Set(new List<DefaultVersion> { new DefaultVersion { Label = "4K", QualityProfileId = 3 }, new DefaultVersion { Label = "HD", QualityProfileId = 2 } });

            Subject.Handle(new MovieAddedEvent(new Movie { Id = 5, Monitored = true }));

            Mocker.GetMock<IVersionSlotService>().Verify(s => s.Add(It.Is<VersionSlot>(v => v.MovieId == 5 && v.Label == "4K" && v.QualityProfileId == 3 && v.Monitored)), Times.Once());
            Mocker.GetMock<IVersionSlotService>().Verify(s => s.Add(It.Is<VersionSlot>(v => v.MovieId == 5 && v.Label == "HD" && v.QualityProfileId == 2)), Times.Once());
        }

        [Test]
        public void should_give_each_imported_movie_the_default_versions_and_survive_a_failure()
        {
            Subject.Set(new List<DefaultVersion> { new DefaultVersion { Label = "4K", QualityProfileId = 3 } });

            Mocker.GetMock<IVersionSlotService>().Setup(s => s.Add(It.Is<VersionSlot>(v => v.MovieId == 1))).Throws(new NzbDroneClientException(System.Net.HttpStatusCode.BadRequest, "no"));

            Subject.Handle(new MoviesImportedEvent(new List<Movie> { new Movie { Id = 1 }, new Movie { Id = 2 } }));

            Mocker.GetMock<IVersionSlotService>().Verify(s => s.Add(It.Is<VersionSlot>(v => v.MovieId == 2)), Times.Once());
            ExceptionVerification.IgnoreWarns();
        }
    }
}
