using System.Collections.Generic;
using NzbDrone.Core.IndexerEngine.Parser.Model;

namespace NzbDrone.Core.IndexerEngine.Indexers
{
    public class IndexerPageableQueryResult
    {
        public IndexerPageableQueryResult()
        {
            Releases = new List<ReleaseInfo>();
            Queries = new List<IndexerQueryResult>();
        }

        public IList<ReleaseInfo> Releases { get; set; }
        public IList<IndexerQueryResult> Queries { get; set; }
    }
}
