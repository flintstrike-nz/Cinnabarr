using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class EngineSeedingOverdueCheckFixture : CoreTest<EngineSeedingOverdueCheck>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                  .Returns("Some Warning Message");
        }

        [Test]
        public void should_return_ok_when_nothing_is_overdue()
        {
            Mocker.GetMock<IEngineSeedingService>().SetupGet(s => s.OverdueTorrents).Returns(new List<string>());

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_return_a_warning_when_a_torrent_is_past_its_goal()
        {
            Mocker.GetMock<IEngineSeedingService>().SetupGet(s => s.OverdueTorrents).Returns(new List<string> { "Some.Show.S01E01" });

            Subject.Check().ShouldBeWarning();
        }
    }
}
