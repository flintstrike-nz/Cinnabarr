using System;
using System.Collections.Generic;
using NzbDrone.Core.IndexerEngine.Parser.Model;

namespace NzbDrone.Core.IndexerEngine.Indexers
{
    public interface IParseIndexerResponse
    {
        IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse);
        Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }
}
