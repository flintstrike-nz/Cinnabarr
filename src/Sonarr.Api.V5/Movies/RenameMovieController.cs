using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Movies;

[V5ApiController("rename/movie")]
public class RenameMovieController : Controller
{
    private readonly IRenameMovieFileService _renameMovieFileService;
    private readonly IMovieService _movieService;
    private readonly IBuildMoviePaths _moviePathBuilder;
    private readonly IRootFolderService _rootFolderService;

    public RenameMovieController(IRenameMovieFileService renameMovieFileService,
                                 IMovieService movieService,
                                 IBuildMoviePaths moviePathBuilder,
                                 IRootFolderService rootFolderService)
    {
        _renameMovieFileService = renameMovieFileService;
        _movieService = movieService;
        _moviePathBuilder = moviePathBuilder;
        _rootFolderService = rootFolderService;
    }

    // Where the movie's folder would be if it followed the current folder format.
    [HttpGet("folder")]
    [Produces("application/json")]
    public Ok<RenameMovieFolderResource> GetFolderPreview([FromQuery] int movieId)
    {
        var movie = _movieService.GetMovie(movieId);

        if (movie.RootFolderPath.IsNullOrWhiteSpace())
        {
            movie.RootFolderPath = _rootFolderService.GetBestRootFolderPath(movie.Path);
        }

        return TypedResults.Ok(new RenameMovieFolderResource
        {
            MovieId = movie.Id,
            ExistingPath = movie.Path,
            NewPath = _moviePathBuilder.BuildPath(movie, false)
        });
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
