using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Storage;
using Sonarr.Http;

namespace Sonarr.Api.V5.Storage;

public class StorageDriveResource
{
    public required string Path { get; set; }
    public long TotalSpace { get; set; }
    public long FreeSpace { get; set; }
    public long IncomingBytes { get; set; }
}

public class StorageMoveResource
{
    public required string MediaType { get; set; }
    public int Id { get; set; }
    public string? Title { get; set; }
    public long Size { get; set; }
    public required string FromDrive { get; set; }
    public required string ToDrive { get; set; }
}

public class StorageLibraryResource
{
    public required string Library { get; set; }
    public List<StorageDriveResource> Drives { get; set; } = new();
    public List<StorageMoveResource> Moves { get; set; } = new();
}

public class StorageSettingsResource
{
    public bool Enabled { get; set; }
    public int IntervalHours { get; set; }
    public int TolerancePercent { get; set; }
    public int ReservePercent { get; set; }
    public int MaxMoveGb { get; set; }
    public int ForecastDays { get; set; }
}

// Drive management: how full each drive of a library is, what the balancer would move tonight, and its settings.
// Running it now is the StorageBalance command.
[V5ApiController("storage")]
public class StorageController : Controller
{
    private readonly IStorageService _storageService;
    private readonly IConfigService _configService;

    public StorageController(IStorageService storageService, IConfigService configService)
    {
        _storageService = storageService;
        _configService = configService;
    }

    [HttpGet("plan")]
    [Produces("application/json")]
    public Ok<List<StorageLibraryResource>> GetPlan()
    {
        return TypedResults.Ok(_storageService.GetPlan().Select(p => new StorageLibraryResource
        {
            Library = p.Library.ToString().ToLowerInvariant(),
            Drives = p.Drives.Select(d => new StorageDriveResource
            {
                Path = d.Path,
                TotalSpace = d.TotalSpace,
                FreeSpace = d.FreeSpace,
                IncomingBytes = p.Incoming.GetValueOrDefault(d.Path)
            }).ToList(),
            Moves = p.Moves.Select(m => new StorageMoveResource
            {
                MediaType = m.Item.MediaType == StorageMediaType.Series ? "series" : "movie",
                Id = m.Item.Id,
                Title = m.Item.Title,
                Size = m.Item.Size,
                FromDrive = m.FromDrive,
                ToDrive = m.ToDrive
            }).ToList()
        }).ToList());
    }

    [HttpGet("settings")]
    [Produces("application/json")]
    public Ok<StorageSettingsResource> GetSettings()
    {
        return TypedResults.Ok(new StorageSettingsResource
        {
            Enabled = _configService.StorageBalancerEnabled,
            IntervalHours = _configService.StorageBalanceIntervalHours,
            TolerancePercent = _configService.StorageTolerancePercent,
            ReservePercent = _configService.StorageReservePercent,
            MaxMoveGb = _configService.StorageMaxMoveGb,
            ForecastDays = _configService.StorageForecastDays
        });
    }

    [HttpPut("settings")]
    [Produces("application/json")]
    public Ok<StorageSettingsResource> PutSettings([FromBody] StorageSettingsResource resource)
    {
        _configService.StorageBalancerEnabled = resource.Enabled;
        _configService.StorageBalanceIntervalHours = Math.Max(1, resource.IntervalHours);
        _configService.StorageTolerancePercent = Math.Clamp(resource.TolerancePercent, 1, 90);
        _configService.StorageReservePercent = Math.Clamp(resource.ReservePercent, 0, 50);
        _configService.StorageMaxMoveGb = Math.Max(1, resource.MaxMoveGb);
        _configService.StorageForecastDays = Math.Clamp(resource.ForecastDays, 0, 90);

        return GetSettings();
    }
}
