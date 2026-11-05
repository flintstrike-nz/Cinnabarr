using System;
using NzbDrone.Core.Indexers.History;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupOldIndexerQueries : IHousekeepingTask
    {
        private const int KeepDays = 30;

        private readonly IIndexerQueryRepository _repository;

        public CleanupOldIndexerQueries(IIndexerQueryRepository repository)
        {
            _repository = repository;
        }

        public void Clean()
        {
            _repository.DeleteOlderThan(DateTime.UtcNow.AddDays(-KeepDays));
        }
    }
}
