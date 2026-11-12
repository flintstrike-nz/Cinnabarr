using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.IndexerEngine.Parser.Model;
using EngineStatus = NzbDrone.Core.IndexerEngine.Indexers.IndexerStatus;
using IEngineStatusService = NzbDrone.Core.IndexerEngine.Indexers.IIndexerStatusService;

namespace NzbDrone.Core.Indexers.Engine
{
    /// <summary>
    /// Gives the indexer engine the status service it expects, backed by Cinnabarr's own indexer status
    /// (back-off, health) so there is one source of truth. Session cookies and the engine's own
    /// "last RSS release" are kept in memory only: they are cheap to rebuild and never need migrating.
    /// </summary>
    public class EngineIndexerStatusService : IEngineStatusService
    {
        private readonly IIndexerStatusService _indexerStatusService;
        private readonly ConcurrentDictionary<int, (IDictionary<string, string> Cookies, DateTime? Expiration)> _cookies = new();
        private readonly ConcurrentDictionary<int, ReleaseInfo> _lastRss = new();

        public EngineIndexerStatusService(IIndexerStatusService indexerStatusService)
        {
            _indexerStatusService = indexerStatusService;
        }

        public List<EngineStatus> GetBlockedProviders()
        {
            return _indexerStatusService.GetBlockedProviders()
                .Select(s => new EngineStatus
                {
                    Id = s.Id,
                    ProviderId = s.ProviderId,
                    InitialFailure = s.InitialFailure,
                    MostRecentFailure = s.MostRecentFailure,
                    EscalationLevel = s.EscalationLevel,
                    DisabledTill = s.DisabledTill
                })
                .ToList();
        }

        public void RecordSuccess(int providerId)
        {
            _indexerStatusService.RecordSuccess(providerId);
        }

        public void RecordFailure(int providerId, TimeSpan minimumBackOff = default)
        {
            _indexerStatusService.RecordFailure(providerId, minimumBackOff);
        }

        public void RecordConnectionFailure(int providerId)
        {
            _indexerStatusService.RecordConnectionFailure(providerId);
        }

        public ReleaseInfo GetLastRssSyncReleaseInfo(int indexerId)
        {
            return _lastRss.TryGetValue(indexerId, out var release) ? release : null;
        }

        public void UpdateRssSyncStatus(int indexerId, ReleaseInfo releaseInfo)
        {
            _lastRss[indexerId] = releaseInfo;
        }

        public IDictionary<string, string> GetIndexerCookies(int indexerId)
        {
            return _cookies.TryGetValue(indexerId, out var entry) ? entry.Cookies : new Dictionary<string, string>();
        }

        public DateTime GetIndexerCookiesExpirationDate(int indexerId)
        {
            return _cookies.TryGetValue(indexerId, out var entry) && entry.Expiration.HasValue ? entry.Expiration.Value : DateTime.MinValue;
        }

        public void UpdateCookies(int indexerId, IDictionary<string, string> cookies, DateTime? expiration)
        {
            _cookies[indexerId] = (cookies, expiration);
        }
    }
}
