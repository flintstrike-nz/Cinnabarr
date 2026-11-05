using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Engine;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using EngineCardigannSettings = NzbDrone.Core.IndexerEngine.Indexers.Definitions.Cardigann.CardigannSettings;
using EngineIndexerDefinition = NzbDrone.Core.IndexerEngine.Indexers.IndexerDefinition;

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
        public void should_offer_one_cardigann_entry_per_definition_instead_of_a_blank_one()
        {
            Mocker.SetConstant<IEnumerable<IIndexer>>(new List<IIndexer> { Mocker.Resolve<NzbDrone.Core.Indexers.Cardigann.Cardigann>() });

            Mocker.GetMock<IEngineIndexerProvider>()
                .Setup(p => p.Templates())
                .Returns(new List<EngineIndexerDefinition>
                {
                    new() { Name = "Alpha", IndexerUrls = new[] { "https://alpha.example" }, Settings = new EngineCardigannSettings { DefinitionFile = "alpha" } },
                    new() { Name = "Beta", Settings = new EngineCardigannSettings { DefinitionFile = "beta" } }
                });

            var definitions = Subject.GetDefaultDefinitions().Where(d => d.Implementation == "Cardigann").ToList();

            definitions.Select(d => d.Name).Should().Equal("Alpha", "Beta");
            definitions.Select(d => d.ImplementationName).Should().Equal("Alpha", "Beta");
            definitions[0].Settings.Should().BeOfType<NzbDrone.Core.Indexers.Cardigann.CardigannSettings>()
                .Which.Should().Match<NzbDrone.Core.Indexers.Cardigann.CardigannSettings>(c => c.DefinitionFile == "alpha" && c.BaseUrl == "https://alpha.example");
            definitions.Should().OnlyContain(d => d.EnableRss && d.ConfigContract == "CardigannSettings");
        }

        [Test]
        public void should_not_throw_when_filling_in_provider_details_for_the_api()
        {
            Subject.Invoking(f => f.SetProviderCharacteristics(_unsupported)).Should().NotThrow();
            _unsupported.ImplementationName.Should().Be("Newznab");
        }
    }
}
