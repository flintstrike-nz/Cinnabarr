using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Indexers.History
{
    public interface IIndexerQueryRepository : IBasicRepository<IndexerQuery>
    {
        List<IndexerQuery> Recent(int? indexerId, int limit);
        List<IndexerQueryStats> Stats(DateTime since);
        void DeleteOlderThan(DateTime date);
        void DeleteForIndexer(int indexerId);
    }

    public class IndexerQueryRepository : BasicRepository<IndexerQuery>, IIndexerQueryRepository
    {
        public IndexerQueryRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<IndexerQuery> Recent(int? indexerId, int limit)
        {
            var all = indexerId.HasValue ? Query(q => q.IndexerId == indexerId.Value) : All();

            return all.OrderByDescending(q => q.Date).Take(limit).ToList();
        }

        public List<IndexerQueryStats> Stats(DateTime since)
        {
            using var connection = _database.OpenConnection();

            return connection.Query<IndexerQueryStats>(
                @"SELECT ""IndexerId"",
                         COUNT(*) AS ""Queries"",
                         SUM(CASE WHEN ""Successful"" THEN 0 ELSE 1 END) AS ""Failures"",
                         CAST(AVG(""ElapsedMs"") AS INTEGER) AS ""AverageElapsedMs"",
                         MAX(""Date"") AS ""LastQuery""
                  FROM ""IndexerQueries""
                  WHERE ""Date"" > @since
                  GROUP BY ""IndexerId""",
                new { since }).ToList();
        }

        public void DeleteOlderThan(DateTime date)
        {
            using var connection = _database.OpenConnection();

            connection.Execute(@"DELETE FROM ""IndexerQueries"" WHERE ""Date"" < @date", new { date });
        }

        public void DeleteForIndexer(int indexerId)
        {
            DeleteMany(Query(q => q.IndexerId == indexerId).Select(q => q.Id).ToList());
        }
    }

    public interface IIndexerQueryService
    {
        /// <summary>Runs a search or RSS fetch on an indexer and records how it went.</summary>
        Task<IList<ReleaseInfo>> Track(IIndexer indexer, IndexerQuerySource source, string query, Func<Task<IList<ReleaseInfo>>> fetch);

        void Record(int indexerId, IndexerQuerySource source, string query, bool successful, int resultCount, TimeSpan elapsed, string error = null);
        List<IndexerQuery> Recent(int? indexerId, int limit);
        List<IndexerQueryStats> Stats(TimeSpan period);
    }

    public class IndexerQueryService : IIndexerQueryService, IHandleAsync<ProviderDeletedEvent<IIndexer>>
    {
        private readonly IIndexerQueryRepository _repository;
        private readonly IIndexerStatusService _indexerStatusService;
        private readonly Logger _logger;

        public IndexerQueryService(IIndexerQueryRepository repository, IIndexerStatusService indexerStatusService, Logger logger)
        {
            _repository = repository;
            _indexerStatusService = indexerStatusService;
            _logger = logger;
        }

        public async Task<IList<ReleaseInfo>> Track(IIndexer indexer, IndexerQuerySource source, string query, Func<Task<IList<ReleaseInfo>>> fetch)
        {
            var indexerId = indexer.Definition.Id;
            var failureBefore = _indexerStatusService.GetMostRecentFailure(indexerId);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var releases = await fetch();

                // Indexers log and swallow their own errors, so a failure shows up as a newer failure in their status.
                var failureAfter = _indexerStatusService.GetMostRecentFailure(indexerId);
                var failed = failureAfter.HasValue && (!failureBefore.HasValue || failureAfter > failureBefore);

                Record(indexerId, source, query, !failed, releases.Count, stopwatch.Elapsed, failed ? "The indexer reported a problem, see the log" : null);

                return releases;
            }
            catch (Exception ex)
            {
                Record(indexerId, source, query, false, 0, stopwatch.Elapsed, ex.Message);

                throw;
            }
        }

        public void Record(int indexerId, IndexerQuerySource source, string query, bool successful, int resultCount, TimeSpan elapsed, string error = null)
        {
            // Keeping a record must never get in the way of a search.
            try
            {
                _repository.Insert(new IndexerQuery
                {
                    IndexerId = indexerId,
                    Date = DateTime.UtcNow,
                    Source = source,
                    Query = query?.Length > 500 ? query[..500] : query,
                    Successful = successful,
                    ResultCount = resultCount,
                    ElapsedMs = (int)Math.Min(elapsed.TotalMilliseconds, int.MaxValue),
                    Error = error?.Length > 500 ? error[..500] : error
                });
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to record the indexer query");
            }
        }

        public List<IndexerQuery> Recent(int? indexerId, int limit)
        {
            return _repository.Recent(indexerId, limit);
        }

        public List<IndexerQueryStats> Stats(TimeSpan period)
        {
            return _repository.Stats(DateTime.UtcNow - period);
        }

        public void HandleAsync(ProviderDeletedEvent<IIndexer> message)
        {
            _repository.DeleteForIndexer(message.ProviderId);
        }
    }
}
