using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Tv.Versions;
using Sonarr.Http;

namespace Sonarr.Api.V5.Wanted;

public class SeriesVersionWantedEpisodeResource
{
    public int EpisodeId { get; set; }
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string? Title { get; set; }
    public DateTime? AirDateUtc { get; set; }
}

public class SeriesVersionWantedResource
{
    public int SeriesId { get; set; }
    public string? SeriesTitle { get; set; }
    public int VersionSlotId { get; set; }
    public string? VersionLabel { get; set; }
    public int QualityProfileId { get; set; }
    public List<SeriesVersionWantedEpisodeResource> Episodes { get; set; } = [];
}

// What the extra versions of series still want: episodes with no file for the version (missing), and episodes whose file
// for the version is below the version's profile cutoff.
[V5ApiController("wanted")]
public class VersionWantedController : Controller
{
    private readonly ISeriesVersionSlotService _versionSlotService;

    public VersionWantedController(ISeriesVersionSlotService versionSlotService)
    {
        _versionSlotService = versionSlotService;
    }

    [HttpGet("missing/versions")]
    [Produces("application/json")]
    public Ok<List<SeriesVersionWantedResource>> GetMissingVersions()
    {
        return TypedResults.Ok(_versionSlotService.GetMissing().Select(ToResource).ToList());
    }

    [HttpGet("cutoff/versions")]
    [Produces("application/json")]
    public Ok<List<SeriesVersionWantedResource>> GetCutoffUnmetVersions()
    {
        return TypedResults.Ok(_versionSlotService.GetCutoffUnmet().Select(ToResource).ToList());
    }

    private static SeriesVersionWantedResource ToResource(SeriesVersionWanted wanted)
    {
        return new SeriesVersionWantedResource
        {
            SeriesId = wanted.Series.Id,
            SeriesTitle = wanted.Series.Title,
            VersionSlotId = wanted.Series.VersionSlotId,
            VersionLabel = wanted.Series.VersionLabel,
            QualityProfileId = wanted.Series.QualityProfileId,
            Episodes = wanted.Episodes
                .OrderBy(e => e.SeasonNumber)
                .ThenBy(e => e.EpisodeNumber)
                .Select(e => new SeriesVersionWantedEpisodeResource
                {
                    EpisodeId = e.Id,
                    SeasonNumber = e.SeasonNumber,
                    EpisodeNumber = e.EpisodeNumber,
                    Title = e.Title,
                    AirDateUtc = e.AirDateUtc
                })
                .ToList()
        };
    }
}
