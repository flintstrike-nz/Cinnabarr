using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaFiles;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Movies;

[V5ApiController("rename/movie")]
public class RenameMovieController : Controller
{
    private readonly IRenameMovieFileService _renameMovieFileService;

    public RenameMovieController(IRenameMovieFileService renameMovieFileService)
    {
        _renameMovieFileService = renameMovieFileService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<RenameMovieResource>> GetPreviews([FromQuery] List<int> movieIds)
    {
        if (movieIds is { Count: 0 })
        {
            throw new BadRequestException("movieIds must be provided");
        }

        return TypedResults.Ok(_renameMovieFileService.GetRenamePreviews(movieIds).ToResource());
    }
}
