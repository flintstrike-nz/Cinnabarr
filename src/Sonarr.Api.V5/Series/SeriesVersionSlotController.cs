using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Versions;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Series;

// A version of a series: the default one (the series' own profile, monitored state and files; id 0) or an extra slot.
public class SeriesVersionSlotResource : RestResource
{
    public int SeriesId { get; set; }
    public string? Label { get; set; }
    public int QualityProfileId { get; set; }
    public bool Monitored { get; set; }
    public bool IsDefault { get; set; }

    // How many of the series' episodes have a file in this version.
    public int EpisodeFileCount { get; set; }
}

[V5ApiController("seriesversion")]
public class SeriesVersionSlotController : RestController<SeriesVersionSlotResource>
{
    private readonly ISeriesVersionSlotService _slotService;
    private readonly ISeriesService _seriesService;
    private readonly IEpisodeService _episodeService;

    public SeriesVersionSlotController(ISeriesVersionSlotService slotService, ISeriesService seriesService, IEpisodeService episodeService)
    {
        _slotService = slotService;
        _seriesService = seriesService;
        _episodeService = episodeService;
    }

    protected override SeriesVersionSlotResource GetResourceById(int id)
    {
        var slot = _slotService.Get(id);

        return ToResource(slot, CountFiles(slot.SeriesId, slot.Id));
    }

    // The default version first (the series' own), then the extra ones.
    [HttpGet]
    [Produces("application/json")]
    public Ok<List<SeriesVersionSlotResource>> GetSlots([FromQuery] int seriesId)
    {
        var series = _seriesService.GetSeries(seriesId);
        var episodes = _episodeService.GetEpisodeBySeries(seriesId);

        var resources = new List<SeriesVersionSlotResource>
        {
            new()
            {
                Id = SeriesVersionSlot.DefaultSlotId,
                SeriesId = series.Id,
                Label = string.Empty,
                QualityProfileId = series.QualityProfileId,
                Monitored = series.Monitored,
                IsDefault = true,
                EpisodeFileCount = episodes.Count(e => e.HasFile)
            }
        };

        resources.AddRange(_slotService.GetBySeries(seriesId).Select(s => ToResource(s, CountFiles(seriesId, s.Id, episodes))));

        return TypedResults.Ok(resources);
    }

    [RestPostById]
    [Consumes("application/json")]
    public Results<Created<SeriesVersionSlotResource>, NotFound> AddSlot([FromBody] SeriesVersionSlotResource resource)
    {
        var slot = _slotService.Add(ToModel(resource));

        return TypedCreated(slot.Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    public Results<Accepted<SeriesVersionSlotResource>, NotFound> UpdateSlot([FromBody] SeriesVersionSlotResource resource)
    {
        _slotService.Update(ToModel(resource));

        return TypedAccepted(resource.Id);
    }

    [RestDeleteById]
    public NoContent DeleteSlot(int id)
    {
        _slotService.Delete(id);

        return TypedResults.NoContent();
    }

    private int CountFiles(int seriesId, int slotId, List<Episode>? episodes = null)
    {
        episodes ??= _episodeService.GetEpisodeBySeries(seriesId);

        return _slotService.GetFiles(episodes.Select(e => e.Id).ToList(), slotId).Count;
    }

    private static SeriesVersionSlotResource ToResource(SeriesVersionSlot slot, int fileCount)
    {
        return new SeriesVersionSlotResource
        {
            Id = slot.Id,
            SeriesId = slot.SeriesId,
            Label = slot.Label,
            QualityProfileId = slot.QualityProfileId,
            Monitored = slot.Monitored,
            IsDefault = false,
            EpisodeFileCount = fileCount
        };
    }

    private static SeriesVersionSlot ToModel(SeriesVersionSlotResource resource)
    {
        return new SeriesVersionSlot
        {
            Id = resource.Id,
            SeriesId = resource.SeriesId,
            Label = resource.Label,
            QualityProfileId = resource.QualityProfileId,
            Monitored = resource.Monitored
        };
    }
}
