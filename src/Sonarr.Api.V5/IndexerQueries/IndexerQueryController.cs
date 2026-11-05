using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.History;
using Sonarr.Http;

namespace Sonarr.Api.V5.IndexerQueries;

public class IndexerQueryResource
{
    public int Id { get; set; }
    public int IndexerId { get; set; }
    public string? IndexerName { get; set; }
    public DateTime Date { get; set; }
    public IndexerQuerySource Source { get; set; }
    public string? Query { get; set; }
    public bool Successful { get; set; }
    public int ResultCount { get; set; }
    public int ElapsedMs { get; set; }
    public string? Error { get; set; }
}

public class IndexerQueryStatsResource
{
    public int IndexerId { get; set; }
    public string? IndexerName { get; set; }
    public int Queries { get; set; }
    public int Failures { get; set; }
    public int AverageElapsedMs { get; set; }
    public DateTime LastQuery { get; set; }
}

// What indexers were asked recently and how they answered (kept for 30 days).
[V5ApiController("indexerquery")]
public class IndexerQueryController : Controller
{
    private const int MaxLimit = 500;

    private readonly IIndexerQueryService _queryService;
    private readonly IIndexerFactory _indexerFactory;

    public IndexerQueryController(IIndexerQueryService queryService, IIndexerFactory indexerFactory)
    {
        _queryService = queryService;
        _indexerFactory = indexerFactory;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<IndexerQueryResource>> GetRecent([FromQuery] int? indexerId = null, [FromQuery] int limit = 100)
    {
        var names = Names();

        return TypedResults.Ok(_queryService.Recent(indexerId, Math.Clamp(limit, 1, MaxLimit))
            .Select(q => new IndexerQueryResource
            {
                Id = q.Id,
                IndexerId = q.IndexerId,
                IndexerName = names.GetValueOrDefault(q.IndexerId),
                Date = q.Date,
                Source = q.Source,
                Query = q.Query,
                Successful = q.Successful,
                ResultCount = q.ResultCount,
                ElapsedMs = q.ElapsedMs,
                Error = q.Error
            })
            .ToList());
    }

    [HttpGet("stats")]
    [Produces("application/json")]
    public Ok<List<IndexerQueryStatsResource>> GetStats([FromQuery] int days = 7)
    {
        var names = Names();

        return TypedResults.Ok(_queryService.Stats(TimeSpan.FromDays(Math.Clamp(days, 1, 30)))
            .Select(s => new IndexerQueryStatsResource
            {
                IndexerId = s.IndexerId,
                IndexerName = names.GetValueOrDefault(s.IndexerId),
                Queries = s.Queries,
                Failures = s.Failures,
                AverageElapsedMs = s.AverageElapsedMs,
                LastQuery = s.LastQuery
            })
            .OrderBy(s => s.IndexerName)
            .ToList());
    }

    private Dictionary<int, string> Names()
    {
        return _indexerFactory.All().ToDictionary(i => i.Id, i => i.Name);
    }
}
