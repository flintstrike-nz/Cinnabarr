using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.IndexerTests
{
    [TestFixture]
    public class IndexerFactoryUnsupportedFixture : CoreTest<IndexerFactory>
    {
        private IndexerDefinition _unsupported;

        [SetUp]
        public void SetUp()
        {
            Mocker.SetConstant<IEnumerable<IIndexer>>(new List<IIndexer>());

            _unsupported = new IndexerDefinition
            {
                Id = 7,
                Name = "Old NZB",
                Implementation = "Newznab",
                Settings = new TorznabSettings { BaseUrl = "http://localhost:9117", Categories = new[] { 5030 } },
                EnableRss = true,
                EnableAutomaticSearch = true,
                EnableInteractiveSearch = true
            };

            Mocker.GetMock<IIndexerRepository>()
                .Setup(v => v.All())
                .Returns(new List<IndexerDefinition> { _unsupported });
        }

        [Test]
        public void should_disable_not_delete_indexers_without_an_implementation_on_startup()
        {
            Subject.Handle(new ApplicationStartedEvent());

            ExceptionVerification.IgnoreWarns();

            Mocker.GetMock<IIndexerRepository>().Verify(v => v.Delete(It.IsAny<IndexerDefinition>()), Times.Never());
            Mocker.GetMock<IIndexerRepository>().Verify(v => v.Update(It.Is<IndexerDefinition>(d => d.Id == 7 && !d.Enable)), Times.Once());
        }

        [Test]
        public void should_list_them_as_unsupported_and_never_instantiate_them()
        {
            Subject.Unsupported().Should().ContainSingle().Which.Name.Should().Be("Old NZB");
            Subject.GetAvailableProviders().Should().BeEmpty();
        }

        [Test]
        public void should_not_throw_when_filling_in_provider_details_for_the_api()
        {
            Subject.Invoking(f => f.SetProviderCharacteristics(_unsupported)).Should().NotThrow();
            _unsupported.ImplementationName.Should().Be("Newznab");
        }
    }
}
