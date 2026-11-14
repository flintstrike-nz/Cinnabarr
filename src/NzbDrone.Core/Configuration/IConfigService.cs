using System.Collections.Generic;
using NzbDrone.Common.Http.Proxy;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.MetadataSource.RadarrMetadata.Resource;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Security;

namespace NzbDrone.Core.Configuration
{
    public interface IConfigService
    {
        void SaveConfigDictionary(Dictionary<string, object> configValues);

        bool IsDefined(string key);

        // Download Client
        string DownloadClientWorkingFolders { get; set; }
        int DownloadClientHistoryLimit { get; set; }

        // Completed/Failed Download Handling (Download client)
        bool EnableCompletedDownloadHandling { get; set; }
        bool EngineUseStagingFolder { get; set; }
        int EngineSeedingMode { get; set; }
        int EngineSeedingMinutes { get; set; }
        double EngineSeedingRatio { get; set; }
        bool EngineOrphanCleanupApproved { get; set; }
        bool EngineLegalNoticeAccepted { get; set; }
        int HistoryCleanupDays { get; set; }
        string IndexerDefinitionsUrl { get; set; }
        string MovieMetadataUrl { get; set; }
        TMDbCountryCode CertificationCountry { get; set; }
        bool LogIndexerResponse { get; set; }
        int EngineOrphanGraceDays { get; set; }

        // How many releases may download the same movie version at once (race mode, roadmap §4.9): 1 is off.
        int EngineRaceMaxSources { get; set; }

        // The versions every new movie gets, as JSON (see DefaultVersionService); empty for none.
        string DefaultMovieVersions { get; set; }

        // The same for new series, as JSON (see DefaultSeriesVersionService); empty for none.
        string DefaultSeriesVersions { get; set; }
        bool AutoRedownloadFailed { get; set; }
        bool AutoRedownloadFailedFromInteractiveSearch { get; set; }

        // Media Management
        bool AutoUnmonitorPreviouslyDownloadedEpisodes { get; set; }

        // Doctor: the scheduled check, how often it runs, and how long a flagged file waits before it is deleted.
        // Update banner: whether to ask GitHub if a newer build of the branch exists, and where.
        bool UpdateCheckEnabled { get; set; }
        string UpdateCheckRepository { get; set; }
        string UpdateCheckBranch { get; set; }

        // Storage: automatic balancing of free space between the drives of a library. Off until switched on.
        bool StorageBalancerEnabled { get; set; }
        int StorageBalanceIntervalHours { get; set; }
        int StorageTolerancePercent { get; set; }
        int StorageReservePercent { get; set; }
        int StorageMaxMoveGb { get; set; }
        int StorageForecastDays { get; set; }

        bool DoctorEnabled { get; set; }
        int DoctorIntervalHours { get; set; }
        int DoctorRetentionDays { get; set; }
        bool AutoUnmonitorPreviouslyDownloadedMovies { get; set; }
        string RecycleBin { get; set; }
        int RecycleBinCleanupDays { get; set; }
        ProperDownloadTypes DownloadPropersAndRepacks { get; set; }
        bool CreateEmptySeriesFolders { get; set; }
        bool CreateEmptyMovieFolders { get; set; }
        bool DeleteEmptyFolders { get; set; }
        FileDateType FileDate { get; set; }
        bool SkipFreeSpaceCheckWhenGrabbing { get; set; }
        bool SkipFreeSpaceCheckWhenImporting { get; set; }
        int MinimumFreeSpaceWhenImporting { get; set; }

        // Days after the movie's minimum availability before it is looked for automatically.
        int AvailabilityDelay { get; set; }
        bool CopyUsingHardlinks { get; set; }
        bool EnableMediaInfo { get; set; }
        bool UseScriptImport { get; set; }
        string ScriptImportPath { get; set; }
        bool ImportExtraFiles { get; set; }
        string ExtraFileExtensions { get; set; }
        RescanAfterRefreshType RescanAfterRefresh { get; set; }
        EpisodeTitleRequiredType EpisodeTitleRequired { get; set; }
        string UserRejectedExtensions { get; set; }

        // Season Pack Upgrade (Media Management)
        SeasonPackUpgradeType SeasonPackUpgrade { get; set; }
        double SeasonPackUpgradeThreshold { get; set; }

        // Permissions (Media Management)
        bool SetPermissionsLinux { get; set; }
        string ChmodFolder { get; set; }
        string ChownGroup { get; set; }

        // Indexers
        int Retention { get; set; }
        int RssSyncInterval { get; set; }
        int MaximumSize { get; set; }
        int MinimumAge { get; set; }

        ListSyncLevelType ListSyncLevel { get; set; }
        int ListSyncTag { get; set; }

        // UI
        int FirstDayOfWeek { get; set; }
        string CalendarWeekColumnHeader { get; set; }

        string ShortDateFormat { get; set; }
        string LongDateFormat { get; set; }
        string TimeFormat { get; set; }
        string TimeZone { get; set; }
        bool ShowRelativeDates { get; set; }
        bool EnableColorImpairedMode { get; set; }
        string HeaderColor { get; set; }
        int UILanguage { get; set; }
        int PreferredMetadataLanguage { get; set; }

        // Internal
        bool CleanupMetadataImages { get; set; }
        string PlexClientIdentifier { get; }

        // Forms Auth
        string RijndaelPassphrase { get; }
        string HmacPassphrase { get; }
        string RijndaelSalt { get; }
        string HmacSalt { get; }

        // Proxy
        bool ProxyEnabled { get; }
        ProxyType ProxyType { get; }
        string ProxyHostname { get; }
        int ProxyPort { get; }
        string ProxyUsername { get; }
        string ProxyPassword { get; }
        string ProxyBypassFilter { get; }
        bool ProxyBypassLocalAddresses { get; }

        // Backups
        string BackupFolder { get; }
        int BackupInterval { get; }
        int BackupRetention { get; }

        CertificateValidationType CertificateValidation { get; }
        string ApplicationUrl { get; }
    }
}
