using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Commands;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Movies;

[V5ApiController]
public class MovieController : RestControllerWithSignalR<MovieResource, Movie>,
                               IHandle<MovieAddedEvent>,
                               IHandle<MovieEditedEvent>,
                               IHandle<MoviesDeletedEvent>,
                               IHandle<MovieFileImportedEvent>,
                               IHandle<MovieFileDeletedEvent>
{
    private readonly IMovieService _movieService;
    private readonly IAddMovieService _addMovieService;
    private readonly IRootFolderService _rootFolderService;
    private readonly IManageCommandQueue _commandQueueManager;

    public MovieController(IBroadcastSignalRMessage signalRBroadcaster,
                           IMovieService movieService,
                           IAddMovieService addMovieService,
                           IRootFolderService rootFolderService,
                           IManageCommandQueue commandQueueManager,
                           RootFolderValidator rootFolderValidator,
                           MappedNetworkDriveValidator mappedNetworkDriveValidator,
                           MoviePathValidator moviePathValidator,
                           MovieExistsValidator movieExistsValidator,
                           MovieAncestorValidator movieAncestorValidator,
                           SystemFolderValidator systemFolderValidator,
                           QualityProfileExistsValidator qualityProfileExistsValidator,
                           RootFolderExistsValidator rootFolderExistsValidator)
        : base(signalRBroadcaster)
    {
        _movieService = movieService;
        _addMovieService = addMovieService;
        _commandQueueManager = commandQueueManager;
        _rootFolderService = rootFolderService;

        SharedValidator.RuleFor(m => m.Path).Cascade(CascadeMode.Stop)
            .IsValidPath()
            .SetValidator(rootFolderValidator)
            .SetValidator(mappedNetworkDriveValidator)
            .SetValidator(moviePathValidator)
            .SetValidator(movieAncestorValidator)
            .SetValidator(systemFolderValidator)
            .When(m => m.Path.IsNotNullOrWhiteSpace());

        PostValidator.RuleFor(m => m.Path).Cascade(CascadeMode.Stop)
            .NotEmpty()
            .IsValidPath()
            .When(m => m.RootFolderPath.IsNullOrWhiteSpace());
        PostValidator.RuleFor(m => m.RootFolderPath).Cascade(CascadeMode.Stop)
            .NotEmpty()
            .IsValidPath()
            .SetValidator(rootFolderExistsValidator)
            .When(m => m.Path.IsNullOrWhiteSpace());

        PutValidator.RuleFor(m => m.Path).Cascade(CascadeMode.Stop)
            .NotEmpty()
            .IsValidPath();

        SharedValidator.RuleFor(m => m.QualityProfileId).Cascade(CascadeMode.Stop)
            .ValidId()
            .SetValidator(qualityProfileExistsValidator);

        PostValidator.RuleFor(m => m.Title).NotEmpty();
        PostValidator.RuleFor(m => m.TmdbId).GreaterThan(0).SetValidator(movieExistsValidator);
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<MovieResource>> AllMovies(int? tmdbId)
    {
        var resources = new List<MovieResource>();

        if (tmdbId.HasValue)
        {
            var movie = _movieService.FindByTmdbId(tmdbId.Value);

            if (movie != null)
            {
                resources.Add(GetMovieResource(movie));
            }
        }
        else
        {
            resources.AddRange(_movieService.GetAllMovies().Select(GetMovieResource));
        }

        return TypedResults.Ok(resources);
    }

    protected override MovieResource? GetResourceById(int id)
    {
        return GetMovieResource(_movieService.GetMovie(id));
    }

    [RestPostById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Results<Created<MovieResource>, NotFound> AddMovie([FromBody] MovieResource movieResource)
    {
        var movie = _addMovieService.AddMovie(movieResource.ToModel());

        return TypedCreated(movie.Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Results<Accepted<MovieResource>, NotFound> UpdateMovie([FromBody] MovieResource movieResource, [FromQuery] bool moveFiles = false)
    {
        var movie = _movieService.GetMovie(movieResource.Id);
        var sourcePath = movie.Path;
        var destinationPath = movieResource.Path;

        _movieService.UpdateMovie(movieResource.ToModel(movie));

        // The movie already holds the new path; the command moves its folder there (and puts the old path back on failure).
        if (moveFiles && destinationPath.IsNotNullOrWhiteSpace() && !sourcePath.PathEquals(destinationPath))
        {
            _commandQueueManager.Push(new MoveMovieCommand
            {
                MovieId = movie.Id,
                SourcePath = sourcePath,
                DestinationPath = destinationPath
            });
        }

        return TypedAccepted(movieResource.Id);
    }

    [RestDeleteById]
    public NoContent DeleteMovie(int id, bool deleteFiles = false, bool addImportListExclusion = false)
    {
        _movieService.DeleteMovie(id, deleteFiles, addImportListExclusion);

        return TypedResults.NoContent();
    }

    private MovieResource GetMovieResource(Movie movie)
    {
        var resource = movie.ToResource();

        resource.RootFolderPath = _rootFolderService.GetBestRootFolderPath(resource.Path ?? string.Empty);

        return resource;
    }

    [NonAction]
    public void Handle(MovieAddedEvent message)
    {
        BroadcastResourceChange(ModelAction.Updated, GetMovieResource(message.Movie));
    }

    [NonAction]
    public void Handle(MovieEditedEvent message)
    {
        BroadcastResourceChange(ModelAction.Updated, GetMovieResource(message.Movie));
    }

    [NonAction]
    public void Handle(MoviesDeletedEvent message)
    {
        foreach (var movie in message.Movies)
        {
            BroadcastResourceChange(ModelAction.Deleted, movie.ToResource());
        }
    }

    [NonAction]
    public void Handle(MovieFileImportedEvent message)
    {
        BroadcastResourceChange(ModelAction.Updated, message.ImportedMovie.MovieId);
    }

    [NonAction]
    public void Handle(MovieFileDeletedEvent message)
    {
        if (message.Reason == DeleteMediaFileReason.Upgrade)
        {
            return;
        }

        BroadcastResourceChange(ModelAction.Updated, message.MovieFile.MovieId);
    }
}
