using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class EngineCheckFixture : CoreTest<EngineCheck>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("Some Warning Message");
        }

        private void GivenStatus(EngineStatus status)
        {
            Mocker.GetMock<IEngineSupervisor>()
                  .SetupGet(s => s.Status)
                  .Returns(status);
        }

        [Test]
        public void should_return_warning_when_engine_not_found()
        {
            GivenStatus(EngineStatus.NotFound);

            Subject.Check().ShouldBeWarning();
        }

        [Test]
        public void should_return_error_when_engine_failed()
        {
            GivenStatus(EngineStatus.Failed);

            Subject.Check().ShouldBeError();
        }

        [TestCase(EngineStatus.NotStarted)]
        [TestCase(EngineStatus.Starting)]
        [TestCase(EngineStatus.Running)]
        public void should_return_ok_otherwise(EngineStatus status)
        {
            GivenStatus(status);

            Subject.Check().ShouldBeOk();
        }
    }
}
