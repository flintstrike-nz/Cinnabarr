using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.History;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests
{
    [TestFixture]
    public class IndexerQueryServiceFixture : CoreTest<IndexerQueryService>
    {
        private Mock<IIndexer> _indexer;
        private IndexerQuery _recorded;

        [SetUp]
        public void SetUp()
        {
            _indexer = new Mock<IIndexer>();
            _indexer.SetupGet(i => i.Definition).Returns(new IndexerDefinition { Id = 4 });

            Mocker.GetMock<IIndexerQueryRepository>()
                .Setup(r => r.Insert(It.IsAny<IndexerQuery>()))
                .Callback<IndexerQuery>(q => _recorded = q)
                .Returns<IndexerQuery>(q => q);
        }

        private static Task<IList<ReleaseInfo>> Releases(int count)
        {
            IList<ReleaseInfo> list = new List<ReleaseInfo>();

            for (var i = 0; i < count; i++)
            {
                list.Add(new ReleaseInfo());
            }

            return Task.FromResult(list);
        }

        [Test]
        public async Task should_record_a_successful_query()
        {
            var result = await Subject.Track(_indexer.Object, IndexerQuerySource.Search, "[Show : S01E01]", () => Releases(3));

            result.Should().HaveCount(3);
            _recorded.IndexerId.Should().Be(4);
            _recorded.Successful.Should().BeTrue();
            _recorded.ResultCount.Should().Be(3);
            _recorded.Query.Should().Be("[Show : S01E01]");
            _recorded.Source.Should().Be(IndexerQuerySource.Search);
        }

        [Test]
        public async Task should_record_a_failure_when_the_indexer_reports_a_new_failure_in_its_status()
        {
            var calls = 0;

            Mocker.GetMock<IIndexerStatusService>()
                .Setup(s => s.GetMostRecentFailure(4))
                .Returns(() => calls++ == 0 ? null : DateTime.UtcNow);

            await Subject.Track(_indexer.Object, IndexerQuerySource.Rss, "RSS sync", () => Releases(0));

            _recorded.Successful.Should().BeFalse();
            _recorded.Error.Should().NotBeNullOrEmpty();
        }

        [Test]
        public void should_record_and_rethrow_when_the_fetch_throws()
        {
            Func<Task> act = () => Subject.Track(_indexer.Object, IndexerQuerySource.Search, "q", () => throw new InvalidOperationException("boom"));

            act.Should().ThrowAsync<InvalidOperationException>().GetAwaiter().GetResult();

            _recorded.Successful.Should().BeFalse();
            _recorded.Error.Should().Be("boom");
        }

        [Test]
        public void should_not_let_a_recording_problem_break_the_search()
        {
            Mocker.GetMock<IIndexerQueryRepository>()
                .Setup(r => r.Insert(It.IsAny<IndexerQuery>()))
                .Throws(new Exception("disk full"));

            Subject.Invoking(s => s.Record(4, IndexerQuerySource.Search, "q", true, 1, TimeSpan.Zero)).Should().NotThrow();
        }
    }
}
