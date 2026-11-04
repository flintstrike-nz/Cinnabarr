using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.FileList;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.IndexerTests
{
    public class IndexerServiceFixture : DbTest<IndexerFactory, IndexerDefinition>
    {
        private List<IIndexer> _indexers;

        [SetUp]
        public void Setup()
        {
            _indexers = new List<IIndexer>();

            _indexers.Add(Mocker.Resolve<Torznab>());
            _indexers.Add(Mocker.Resolve<FileList>());

            Mocker.SetConstant<IEnumerable<IIndexer>>(_indexers);
        }

        [Test]
        public void should_remove_missing_indexers_on_startup()
        {
            var repo = Mocker.Resolve<IndexerRepository>();

            Mocker.SetConstant<IIndexerRepository>(repo);

            var existingIndexers = Builder<IndexerDefinition>.CreateNew().BuildNew();
            existingIndexers.ConfigContract = nameof(TorznabSettings);

            repo.Insert(existingIndexers);

            Subject.Handle(new ApplicationStartedEvent());

            AllStoredModels.Should().NotContain(c => c.Id == existingIndexers.Id);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_set_default_fail_downloads_on_default_definitions()
        {
            var definitions = Subject.GetDefaultDefinitions().ToList();

            definitions.Single(d => d.Implementation == nameof(Torznab)).Settings.As<TorznabSettings>().FailDownloads
                .Should().BeEquivalentTo([(int)FailDownloads.Executables, (int)FailDownloads.PotentiallyDangerous]);

            definitions.Single(d => d.Implementation == nameof(FileList)).Settings.As<FileListSettings>().FailDownloads
                .Should().BeEmpty();
        }
    }
}
