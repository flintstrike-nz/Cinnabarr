using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class NewVersionServiceFixture : CoreTest<NewVersionService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.UpdateCheckEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.UpdateCheckRepository).Returns("owner/repo");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.UpdateCheckBranch).Returns("develop");
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("CINNABARR_COMMIT", null);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("unknown")]
        public void should_not_check_without_a_build_commit(string commit)
        {
            Environment.SetEnvironmentVariable("CINNABARR_COMMIT", commit);

            Subject.Check().Available.Should().BeFalse();

            Mocker.GetMock<IHttpClient>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_not_check_when_switched_off()
        {
            Environment.SetEnvironmentVariable("CINNABARR_COMMIT", "abc123");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.UpdateCheckEnabled).Returns(false);

            Subject.Check().Available.Should().BeFalse();

            Mocker.GetMock<IHttpClient>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_say_nothing_when_the_check_fails()
        {
            Environment.SetEnvironmentVariable("CINNABARR_COMMIT", "abc123");
            Mocker.GetMock<IHttpClient>().Setup(c => c.Execute(It.IsAny<HttpRequest>())).Throws(new Exception("no network"));

            var info = Subject.Check();

            info.Available.Should().BeFalse();
            info.Current.Should().Be("abc123");
        }
    }
}
