using NzbDrone.Core.Configuration;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Settings;

public class DownloadClientSettingsResource : RestResource
{
    public string? DownloadClientWorkingFolders { get; set; }
    public bool EnableCompletedDownloadHandling { get; set; }
    public bool EngineUseStagingFolder { get; set; }
    public int EngineSeedingMode { get; set; }
    public int EngineSeedingMinutes { get; set; }
    public double EngineSeedingRatio { get; set; }
    public bool AutoRedownloadFailed { get; set; }
    public bool AutoRedownloadFailedFromInteractiveSearch { get; set; }
}

public static class DownloadClientSettingsResourceMapper
{
    public static DownloadClientSettingsResource ToResource(IConfigService model)
    {
        return new DownloadClientSettingsResource
        {
            DownloadClientWorkingFolders = model.DownloadClientWorkingFolders,
            EnableCompletedDownloadHandling = model.EnableCompletedDownloadHandling,
            EngineUseStagingFolder = model.EngineUseStagingFolder,
            EngineSeedingMode = model.EngineSeedingMode,
            EngineSeedingMinutes = model.EngineSeedingMinutes,
            EngineSeedingRatio = model.EngineSeedingRatio,
            AutoRedownloadFailed = model.AutoRedownloadFailed,
            AutoRedownloadFailedFromInteractiveSearch = model.AutoRedownloadFailedFromInteractiveSearch
        };
    }
}
