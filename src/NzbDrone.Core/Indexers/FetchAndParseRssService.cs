using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Indexers.History;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers
{
    public interface IFetchAndParseRss
    {
        Task<List<ReleaseInfo>> Fetch();
        Task<List<ReleaseInfo>> FetchMovies();
    }

    public class FetchAndParseRssService : IFetchAndParseRss
    {
        private readonly IIndexerFactory _indexerFactory;
        private readonly IIndexerQueryService _indexerQueryService;
        private readonly Logger _logger;

        public FetchAndParseRssService(IIndexerFactory indexerFactory, IIndexerQueryService indexerQueryService, Logger logger)
        {
            _indexerFactory = indexerFactory;
            _indexerQueryService = indexerQueryService;
            _logger = logger;
        }

        public Task<List<ReleaseInfo>> Fetch()
        {
            return Fetch(indexer => indexer.FetchRecent());
        }

        public Task<List<ReleaseInfo>> FetchMovies()
        {
            return Fetch(indexer => indexer.FetchRecentMovies());
        }

        private async Task<List<ReleaseInfo>> Fetch(Func<IIndexer, Task<IList<ReleaseInfo>>> fetch)
        {
            var indexers = _indexerFactory.RssEnabled();

            if (!indexers.Any())
            {
                _logger.Warn("No available indexers. check your configuration.");

                return new List<ReleaseInfo>();
            }

            _logger.Debug("Available indexers {0}", indexers.Count);

            var tasks = indexers.Select(i => FetchIndexer(i, fetch));

            var batch = await Task.WhenAll(tasks);

            var result = batch.SelectMany(x => x).ToList();

            _logger.Debug("Found {0} reports", result.Count);

            return result;
        }

        private async Task<IList<ReleaseInfo>> FetchIndexer(IIndexer indexer, Func<IIndexer, Task<IList<ReleaseInfo>>> fetch)
        {
            try
            {
                return await _indexerQueryService.Track(indexer, IndexerQuerySource.Rss, "RSS sync", () => fetch(indexer));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during RSS Sync");
            }

            return Array.Empty<ReleaseInfo>();
        }
    }
}
