using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Qualities;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Movies;

public class MovieHistoryResource : RestResource
{
    public int MovieId { get; set; }
    public string? MovieTitle { get; set; }
    public required string SourceTitle { get; set; }
    public required List<Language> Languages { get; set; }
    public required QualityModel Quality { get; set; }
    public DateTime Date { get; set; }
    public string? DownloadId { get; set; }
    public MovieHistoryEventType EventType { get; set; }
    public required Dictionary<string, string> Data { get; set; }
}

public class MovieBlocklistResource : RestResource
{
    public int MovieId { get; set; }
    public string? MovieTitle { get; set; }
    public required string SourceTitle { get; set; }
    public required List<Language> Languages { get; set; }
    public required QualityModel Quality { get; set; }
    public DateTime Date { get; set; }
    public DownloadProtocol Protocol { get; set; }
    public string? Indexer { get; set; }
    public string? Message { get; set; }
}

[V5ApiController("history/movie")]
public class MovieHistoryController : Controller
{
    private const int RecentLimit = 200;

    private readonly IMovieHistoryService _historyService;
    private readonly IMovieService _movieService;

    public MovieHistoryController(IMovieHistoryService historyService, IMovieService movieService)
    {
        _historyService = historyService;
        _movieService = movieService;
    }

    // With a movieId: that movie's events. Without one: the most recent events of all movies (last 30 days, capped).
    [HttpGet]
    [Produces("application/json")]
    public Ok<List<MovieHistoryResource>> GetMovieHistory(int? movieId)
    {
        var items = movieId.HasValue
            ? _historyService.GetByMovieId(movieId.Value)
            : _historyService.Since(DateTime.UtcNow.AddDays(-30), null).OrderByDescending(h => h.Date).Take(RecentLimit).ToList();

        var titles = items.Select(h => h.MovieId).Distinct().ToList() switch
        {
            { Count: > 0 } ids => _movieService.GetMovies(ids).ToDictionary(m => m.Id, m => m.Title),
            _ => new Dictionary<int, string>()
        };

        return TypedResults.Ok(items.Select(h => new MovieHistoryResource
        {
            Id = h.Id,
            MovieId = h.MovieId,
            MovieTitle = titles.GetValueOrDefault(h.MovieId),
            SourceTitle = h.SourceTitle,
            Languages = h.Languages ?? [],
            Quality = h.Quality,
            Date = h.Date,
            DownloadId = h.DownloadId,
            EventType = h.EventType,
            Data = h.Data
        }).ToList());
    }
}

[V5ApiController("blocklist/movie")]
public class MovieBlocklistController : Controller
{
    private readonly IMovieBlocklistService _blocklistService;
    private readonly IMovieService _movieService;

    public MovieBlocklistController(IMovieBlocklistService blocklistService, IMovieService movieService)
    {
        _blocklistService = blocklistService;
        _movieService = movieService;
    }

    // With a movieId: that movie's blocklisted releases. Without one: all of them, newest first.
    [HttpGet]
    [Produces("application/json")]
    public Ok<List<MovieBlocklistResource>> GetMovieBlocklist(int? movieId)
    {
        var items = movieId.HasValue ? _blocklistService.GetByMovieId(movieId.Value) : _blocklistService.GetAll();

        var titles = items.Select(b => b.MovieId).Distinct().ToList() switch
        {
            { Count: > 0 } ids => _movieService.GetMovies(ids).ToDictionary(m => m.Id, m => m.Title),
            _ => new Dictionary<int, string>()
        };

        return TypedResults.Ok(items.Select(b => new MovieBlocklistResource
        {
            Id = b.Id,
            MovieId = b.MovieId,
            MovieTitle = titles.GetValueOrDefault(b.MovieId),
            SourceTitle = b.SourceTitle,
            Languages = b.Languages ?? [],
            Quality = b.Quality,
            Date = b.Date,
            Protocol = b.Protocol,
            Indexer = b.Indexer,
            Message = b.Message
        }).ToList());
    }

    [HttpDelete("{id:int}")]
    public NoContent RemoveFromMovieBlocklist([FromRoute] int id)
    {
        _blocklistService.Delete(id);

        return TypedResults.NoContent();
    }
}
