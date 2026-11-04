using NzbDrone.Core.Configuration;
using Sonarr.Http.REST;

namespace Sonarr.Api.V3.Config
{
    public class DownloadClientConfigResource : RestResource
    {
        public string DownloadClientWorkingFolders { get; set; }

        public bool EnableCompletedDownloadHandling { get; set; }

        // Nullable, so a client that doesn't send them leaves the saved values as they are.
        public bool? EngineUseStagingFolder { get; set; }
        public int? EngineSeedingMode { get; set; }
        public int? EngineSeedingMinutes { get; set; }
        public double? EngineSeedingRatio { get; set; }
        public bool? EngineOrphanCleanupApproved { get; set; }
        public int? EngineOrphanGraceDays { get; set; }
        public bool AutoRedownloadFailed { get; set; }
        public bool AutoRedownloadFailedFromInteractiveSearch { get; set; }
    }

    public static class DownloadClientConfigResourceMapper
    {
        public static DownloadClientConfigResource ToResource(IConfigService model)
        {
            return new DownloadClientConfigResource
            {
                DownloadClientWorkingFolders = model.DownloadClientWorkingFolders,

                EnableCompletedDownloadHandling = model.EnableCompletedDownloadHandling,
                EngineUseStagingFolder = model.EngineUseStagingFolder,
                EngineSeedingMode = model.EngineSeedingMode,
                EngineSeedingMinutes = model.EngineSeedingMinutes,
                EngineSeedingRatio = model.EngineSeedingRatio,
                EngineOrphanCleanupApproved = model.EngineOrphanCleanupApproved,
                EngineOrphanGraceDays = model.EngineOrphanGraceDays,
                AutoRedownloadFailed = model.AutoRedownloadFailed,
                AutoRedownloadFailedFromInteractiveSearch = model.AutoRedownloadFailedFromInteractiveSearch
            };
        }
    }
}
