using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Movies;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Movies;

public class UnmappedMovieFolderResource
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public required string ParsedTitle { get; set; }
    public int ParsedYear { get; set; }
}

// Library import: the folders of a root folder that hold no movie of the library yet. Each is added with the
// ordinary add-movie call, giving the folder as the movie's path.
[V5ApiController("movie/import")]
public class MovieImportController : Controller
{
    private readonly IUnmappedMovieFolderService _unmappedMovieFolderService;

    public MovieImportController(IUnmappedMovieFolderService unmappedMovieFolderService)
    {
        _unmappedMovieFolderService = unmappedMovieFolderService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<UnmappedMovieFolderResource>> GetUnmappedFolders([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new BadRequestException("path must be provided");
        }

        return TypedResults.Ok(_unmappedMovieFolderService.Get(path).Select(f => new UnmappedMovieFolderResource
        {
            Name = f.Name,
            Path = f.Path,
            ParsedTitle = f.ParsedTitle,
            ParsedYear = f.ParsedYear
        }).ToList());
    }
}
