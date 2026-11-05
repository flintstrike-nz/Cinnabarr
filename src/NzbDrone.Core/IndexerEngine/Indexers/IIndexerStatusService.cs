using System;
using System.Collections.Generic;
using NzbDrone.Core.IndexerEngine.Parser.Model;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.IndexerEngine.Indexers
{
    // EMBARR: only Prowlarr's interface is kept. The implementation is Embarr's EngineIndexerStatusService, which
    // records into Embarr's own indexer status (roadmap Phase 3, step 3b).
    public interface IIndexerStatusService : IProviderStatusServiceBase<IndexerStatus>
    {
        ReleaseInfo GetLastRssSyncReleaseInfo(int indexerId);
        IDictionary<string, string> GetIndexerCookies(int indexerId);
        DateTime GetIndexerCookiesExpirationDate(int indexerId);

        void UpdateRssSyncStatus(int indexerId, ReleaseInfo releaseInfo);
        void UpdateCookies(int indexerId, IDictionary<string, string> cookies, DateTime? expiration);
    }
}
