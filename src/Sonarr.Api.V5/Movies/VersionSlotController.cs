using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Movies;

[V5ApiController("versionslot")]
public class VersionSlotController : RestController<VersionSlotResource>
{
    private readonly IVersionSlotService _slotService;
    private readonly IMovieService _movieService;
    private readonly IMovieFileService _movieFileService;

    public VersionSlotController(IVersionSlotService slotService, IMovieService movieService, IMovieFileService movieFileService)
    {
        _slotService = slotService;
        _movieService = movieService;
        _movieFileService = movieFileService;
    }

    protected override VersionSlotResource GetResourceById(int id)
    {
        var slot = _slotService.Get(id);

        return ToResource(slot, _movieFileService.GetFilesByMovie(slot.MovieId));
    }

    // The default version first (the movie's own), then the extra ones.
    [HttpGet]
    [Produces("application/json")]
    public Ok<List<VersionSlotResource>> GetSlots([FromQuery] int movieId)
    {
        var movie = _movieService.GetMovie(movieId);
        var files = _movieFileService.GetFilesByMovie(movieId);

        var resources = new List<VersionSlotResource>
        {
            new()
            {
                Id = VersionSlot.DefaultSlotId,
                MovieId = movie.Id,
                Label = string.Empty,
                QualityProfileId = movie.QualityProfileId,
                Monitored = movie.Monitored,
                IsDefault = true,
                MovieFileId = movie.MovieFileId
            }
        };

        resources.AddRange(_slotService.GetByMovie(movieId).Select(s => ToResource(s, files)));

        return TypedResults.Ok(resources);
    }

    [RestPostById]
    [Consumes("application/json")]
    public Results<Created<VersionSlotResource>, NotFound> AddSlot([FromBody] VersionSlotResource resource)
    {
        var slot = _slotService.Add(resource.ToModel());

        return TypedCreated(slot.Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    public Results<Accepted<VersionSlotResource>, NotFound> UpdateSlot([FromBody] VersionSlotResource resource)
    {
        _slotService.Update(resource.ToModel());

        return TypedAccepted(resource.Id);
    }

    [RestDeleteById]
    public NoContent DeleteSlot(int id)
    {
        _slotService.Delete(id);

        return TypedResults.NoContent();
    }

    private static VersionSlotResource ToResource(VersionSlot slot, List<MovieFile> files)
    {
        return new VersionSlotResource
        {
            Id = slot.Id,
            MovieId = slot.MovieId,
            Label = slot.Label,
            QualityProfileId = slot.QualityProfileId,
            Monitored = slot.Monitored,
            IsDefault = false,
            MovieFileId = files.FirstOrDefault(f => f.VersionSlotId == slot.Id)?.Id ?? 0
        };
    }
}
