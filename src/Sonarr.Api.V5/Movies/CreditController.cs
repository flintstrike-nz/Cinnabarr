using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Credits;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Movies;

public class CreditResource : RestResource
{
    public required string Name { get; set; }
    public string? Character { get; set; }
    public string? Job { get; set; }
    public string? Department { get; set; }
    public int Order { get; set; }

    // "cast" or "crew".
    public required string Type { get; set; }
}

[V5ApiController("credit")]
public class CreditController : Controller
{
    private readonly ICreditService _creditService;
    private readonly IMovieService _movieService;

    public CreditController(ICreditService creditService, IMovieService movieService)
    {
        _creditService = creditService;
        _movieService = movieService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<CreditResource>> GetCredits(int movieId)
    {
        if (movieId <= 0)
        {
            throw new BadRequestException("movieId must be provided");
        }

        var movie = _movieService.GetMovie(movieId);

        return TypedResults.Ok(_creditService.GetAllCreditsForMovieMetadata(movie.MovieMetadataId)
            .OrderBy(c => c.Type)
            .ThenBy(c => c.Order)
            .Select(c => new CreditResource
            {
                Id = c.Id,
                Name = c.Name,
                Character = c.Character,
                Job = c.Job,
                Department = c.Department,
                Order = c.Order,
                Type = c.Type == CreditType.Cast ? "cast" : "crew"
            })
            .ToList());
    }
}
