using System;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers
{
    public interface ISeedConfigProvider
    {
        TorrentSeedConfiguration GetSeedConfiguration(RemoteMedia release);
        TorrentSeedConfiguration GetSeedConfiguration(int indexerId, bool fullSeason);
    }

    public class SeedConfigProvider : ISeedConfigProvider
    {
        private readonly ICachedIndexerSettingsProvider _cachedIndexerSettingsProvider;

        public SeedConfigProvider(ICachedIndexerSettingsProvider cachedIndexerSettingsProvider)
        {
            _cachedIndexerSettingsProvider = cachedIndexerSettingsProvider;
        }

        public TorrentSeedConfiguration GetSeedConfiguration(RemoteMedia remote)
        {
            if (remote.Release.DownloadProtocol != DownloadProtocol.Torrent)
            {
                return null;
            }

            if (remote.Release.IndexerId == 0)
            {
                return null;
            }

            // Only a series release can be a full season; a movie never uses the season pack goal.
            var fullSeason = remote is RemoteEpisode { ParsedEpisodeInfo: { } parsed } && parsed.FullSeason;

            return GetSeedConfiguration(remote.Release.IndexerId, fullSeason);
        }

        public TorrentSeedConfiguration GetSeedConfiguration(int indexerId, bool fullSeason)
        {
            if (indexerId == 0)
            {
                return null;
            }

            var settings = _cachedIndexerSettingsProvider.GetSettings(indexerId);
            var seedCriteria = settings?.SeedCriteriaSettings;

            if (seedCriteria == null)
            {
                return null;
            }

            var useSeasonPackSeedGoal = (SeasonPackSeedGoal)seedCriteria.SeasonPackSeedGoal == SeasonPackSeedGoal.UseSeasonPackSeedGoal;

            var seedConfig = new TorrentSeedConfiguration
            {
                Ratio = (fullSeason && useSeasonPackSeedGoal)
                    ? seedCriteria.SeasonPackSeedRatio
                    : seedCriteria.SeedRatio
            };

            var seedTime = (fullSeason && useSeasonPackSeedGoal) ? seedCriteria.SeasonPackSeedTime : seedCriteria.SeedTime;
            if (seedTime.HasValue)
            {
                seedConfig.SeedTime = TimeSpan.FromMinutes(seedTime.Value);
            }

            return seedConfig;
        }
    }
}
