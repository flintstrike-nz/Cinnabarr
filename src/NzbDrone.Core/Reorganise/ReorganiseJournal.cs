using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Reorganise
{
    public static class ReorganiseMediaTypes
    {
        public const string Movie = "movie";
        public const string Episode = "episode";
    }

    // One file the reorganise tool renamed.
    public class ReorganiseJournalEntry : ModelBase
    {
        public string RunId { get; set; }
        public string MediaType { get; set; }
        public int MediaId { get; set; }
        public int FileId { get; set; }
        public string Title { get; set; }
        public string FromPath { get; set; }
        public string ToPath { get; set; }
        public DateTime Applied { get; set; }
        public DateTime? Undone { get; set; }
    }

    public interface IReorganiseJournalRepository : IBasicRepository<ReorganiseJournalEntry>
    {
        List<ReorganiseJournalEntry> GetByRun(string runId);
    }

    public class ReorganiseJournalRepository : BasicRepository<ReorganiseJournalEntry>, IReorganiseJournalRepository
    {
        public ReorganiseJournalRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<ReorganiseJournalEntry> GetByRun(string runId)
        {
            return Query(e => e.RunId == runId).OrderBy(e => e.Id).ToList();
        }
    }
}
