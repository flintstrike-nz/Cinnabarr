using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Qualities;
using Sonarr.Api.V5.EpisodeFiles;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Movies;

public class MovieFileResource : RestResource
{
    public int MovieId { get; set; }
    public string? RelativePath { get; set; }
    public string? Path { get; set; }
    public long Size { get; set; }
    public DateTime DateAdded { get; set; }
    public string? SceneName { get; set; }
    public string? ReleaseGroup { get; set; }
    public string? Edition { get; set; }
    public List<Language> Languages { get; set; } = [];
    public QualityModel? Quality { get; set; }
    public MediaInfoResource? MediaInfo { get; set; }
}

[V5ApiController("moviefile")]
public class MovieFileController : Controller
{
    private readonly IMovieFileService _movieFileService;
    private readonly IMovieService _movieService;
    private readonly IDeleteMovieFiles _deleteMovieFiles;

    public MovieFileController(IMovieFileService movieFileService, IMovieService movieService, IDeleteMovieFiles deleteMovieFiles)
    {
        _movieFileService = movieFileService;
        _movieService = movieService;
        _deleteMovieFiles = deleteMovieFiles;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<MovieFileResource>> GetMovieFiles(int movieId)
    {
        if (movieId <= 0)
        {
            throw new BadRequestException("movieId must be provided");
        }

        var movie = _movieService.GetMovie(movieId);

        return TypedResults.Ok(_movieFileService.GetFilesByMovie(movieId).Select(f => ToResource(movie, f)).ToList());
    }

    [HttpDelete("{id:int}")]
    public Results<NoContent, NotFound> DeleteMovieFile(int id)
    {
        var movieFile = _movieFileService.GetMovie(id);

        if (movieFile == null)
        {
            return TypedResults.NotFound();
        }

        _deleteMovieFiles.DeleteMovieFile(_movieService.GetMovie(movieFile.MovieId), movieFile);

        return TypedResults.NoContent();
    }

    private static MovieFileResource ToResource(Movie movie, MovieFile file)
    {
        return new MovieFileResource
        {
            Id = file.Id,
            MovieId = file.MovieId,
            RelativePath = file.RelativePath,
            Path = global::System.IO.Path.Combine(movie.Path, file.RelativePath),
            Size = file.Size,
            DateAdded = file.DateAdded,
            SceneName = file.SceneName,
            ReleaseGroup = file.ReleaseGroup,
            Edition = file.Edition,
            Languages = file.Languages ?? [],
            Quality = file.Quality,
            MediaInfo = file.MediaInfo.ToResource(file.SceneName)
        };
    }
}
