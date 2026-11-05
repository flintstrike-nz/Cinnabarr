using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class IndexerUnsupportedCheckFixture : CoreTest<IndexerUnsupportedCheck>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<ILocalizationService>()
                .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .Returns("Unsupported indexers");
        }

        [Test]
        public void should_not_warn_when_all_indexers_are_supported()
        {
            Mocker.GetMock<IIndexerFactory>()
                .Setup(v => v.Unsupported())
                .Returns(new List<IndexerDefinition>());

            Subject.Check().Type.Should().Be(HealthCheckResult.Ok);
        }

        [Test]
        public void should_warn_and_name_unsupported_indexers()
        {
            Mocker.GetMock<IIndexerFactory>()
                .Setup(v => v.Unsupported())
                .Returns(new List<IndexerDefinition> { new IndexerDefinition { Name = "Old NZB", Implementation = "Newznab" } });

            var result = Subject.Check();

            result.Type.Should().Be(HealthCheckResult.Warning);
            result.Reason.Should().Be(HealthCheckReason.IndexerUnsupported);
        }
    }
}
