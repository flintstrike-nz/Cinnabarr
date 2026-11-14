using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Doctor;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Tv;
using Sonarr.Http;

namespace Sonarr.Api.V5.Doctor;

public class DoctorFileResource
{
    public int Id { get; set; }
    public string? Path { get; set; }
    public long Size { get; set; }
    public string? Quality { get; set; }
    public int Resolution { get; set; }
    public long VideoBitrate { get; set; }
    public string? VideoCodec { get; set; }
    public DateTime DateAdded { get; set; }
}

public class DoctorFlagResource
{
    public int Id { get; set; }
    public required string MediaType { get; set; }
    public int ItemId { get; set; }
    public string? Title { get; set; }
    public string? Reason { get; set; }
    public DateTime FlaggedAt { get; set; }
    public DateTime DeleteAfter { get; set; }
    public DoctorFileResource? Flagged { get; set; }
    public DoctorFileResource? Kept { get; set; }
}

public class DoctorSettingsResource
{
    public bool Enabled { get; set; }
    public int IntervalHours { get; set; }
    public int RetentionDays { get; set; }
}

// Doctor (library health): the worse duplicate copies it flagged, what to do with them, and its schedule. Running it is
// the DoctorCommand.
[V5ApiController("doctor")]
public class DoctorController : Controller
{
    private readonly IDoctorService _doctorService;
    private readonly IConfigService _configService;
    private readonly ISeriesService _seriesService;
    private readonly IMovieService _movieService;
    private readonly IMediaFileService _episodeFileService;
    private readonly IMovieFileService _movieFileService;

    public DoctorController(IDoctorService doctorService,
                            IConfigService configService,
                            ISeriesService seriesService,
                            IMovieService movieService,
                            IMediaFileService episodeFileService,
                            IMovieFileService movieFileService)
    {
        _doctorService = doctorService;
        _configService = configService;
        _seriesService = seriesService;
        _movieService = movieService;
        _episodeFileService = episodeFileService;
        _movieFileService = movieFileService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<DoctorFlagResource>> GetFlags()
    {
        var retention = Math.Max(1, _configService.DoctorRetentionDays);
        var list = new List<DoctorFlagResource>();

        foreach (var flag in _doctorService.GetFlags())
        {
            var resource = new DoctorFlagResource
            {
                Id = flag.Id,
                MediaType = flag.MediaType == DoctorMediaType.Series ? "series" : "movie",
                ItemId = flag.ItemId,
                Reason = flag.Reason,
                FlaggedAt = flag.FlaggedAt,
                DeleteAfter = flag.FlaggedAt.AddDays(retention)
            };

            if (flag.MediaType == DoctorMediaType.Series)
            {
                resource.Title = _seriesService.GetSeries(flag.ItemId)?.Title;
                resource.Flagged = ToResource(_episodeFileService.Get(flag.FileId));
                resource.Kept = ToResource(_episodeFileService.Get(flag.KeptFileId));
            }
            else
            {
                resource.Title = _movieService.GetMovie(flag.ItemId)?.Title;
                resource.Flagged = ToResource(_movieFileService.GetMovie(flag.FileId));
                resource.Kept = ToResource(_movieFileService.GetMovie(flag.KeptFileId));
            }

            list.Add(resource);
        }

        return TypedResults.Ok(list);
    }

    [HttpPost("{id:int}/recover")]
    public Ok Recover([FromRoute] int id)
    {
        _doctorService.Recover(id);

        return TypedResults.Ok();
    }

    [HttpDelete("{id:int}")]
    public Ok Delete([FromRoute] int id)
    {
        _doctorService.Delete(id);

        return TypedResults.Ok();
    }

    [HttpGet("settings")]
    [Produces("application/json")]
    public Ok<DoctorSettingsResource> GetSettings()
    {
        return TypedResults.Ok(new DoctorSettingsResource
        {
            Enabled = _configService.DoctorEnabled,
            IntervalHours = _configService.DoctorIntervalHours,
            RetentionDays = _configService.DoctorRetentionDays
        });
    }

    [HttpPut("settings")]
    [Produces("application/json")]
    public Ok<DoctorSettingsResource> PutSettings([FromBody] DoctorSettingsResource resource)
    {
        _configService.DoctorEnabled = resource.Enabled;
        _configService.DoctorIntervalHours = Math.Max(1, resource.IntervalHours);
        _configService.DoctorRetentionDays = Math.Max(1, resource.RetentionDays);

        return GetSettings();
    }

    private static DoctorFileResource? ToResource(EpisodeFile? file)
    {
        return file == null ? null : ToResource(file.Id, file.Path ?? file.RelativePath, file.Size, file.Quality?.Quality.Name, file.MediaInfo, file.DateAdded);
    }

    private static DoctorFileResource? ToResource(MovieFile? file)
    {
        return file == null ? null : ToResource(file.Id, file.Path ?? file.RelativePath, file.Size, file.Quality?.Quality.Name, file.MediaInfo, file.DateAdded);
    }

    private static DoctorFileResource ToResource(int id, string? path, long size, string? quality, NzbDrone.Core.MediaFiles.MediaInfo.MediaInfoModel? info, DateTime dateAdded)
    {
        return new DoctorFileResource
        {
            Id = id,
            Path = path,
            Size = size,
            Quality = quality,
            Resolution = info?.Height ?? 0,
            VideoBitrate = info?.VideoBitrate ?? 0,
            VideoCodec = info?.VideoFormat,
            DateAdded = dateAdded
        };
    }
}
