using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv.Versions
{
    public interface IDefaultSeriesVersionService
    {
        List<DefaultVersion> Get();
        void Set(List<DefaultVersion> versions);
    }

    /// <summary>The versions a series gets when it is added (or imported), like <see cref="DefaultVersionService"/> for movies.</summary>
    public class DefaultSeriesVersionService : IDefaultSeriesVersionService, IHandle<SeriesAddedEvent>, IHandle<SeriesImportedEvent>
    {
        private readonly IConfigService _configService;
        private readonly ISeriesVersionSlotService _versionSlotService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly ISeriesService _seriesService;
        private readonly Logger _logger;

        public DefaultSeriesVersionService(IConfigService configService,
                                           ISeriesVersionSlotService versionSlotService,
                                           IQualityProfileService qualityProfileService,
                                           ISeriesService seriesService,
                                           Logger logger)
        {
            _configService = configService;
            _versionSlotService = versionSlotService;
            _qualityProfileService = qualityProfileService;
            _seriesService = seriesService;
            _logger = logger;
        }

        public List<DefaultVersion> Get()
        {
            var json = _configService.DefaultSeriesVersions;

            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<DefaultVersion>();
            }

            try
            {
                return Json.Deserialize<List<DefaultVersion>>(json) ?? new List<DefaultVersion>();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "The default series versions couldn't be read and are ignored");

                return new List<DefaultVersion>();
            }
        }

        public void Set(List<DefaultVersion> versions)
        {
            versions = (versions ?? new List<DefaultVersion>()).Select(v => new DefaultVersion { Label = v.Label?.Trim(), QualityProfileId = v.QualityProfileId }).ToList();

            if (versions.Count >= SeriesVersionSlotService.MaxSlotsPerSeries)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "At most {0} default versions can be set", SeriesVersionSlotService.MaxSlotsPerSeries - 1);
            }

            if (versions.Any(v => string.IsNullOrWhiteSpace(v.Label)))
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "Every default version needs a label");
            }

            if (versions.GroupBy(v => v.Label, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "Default version labels must be different");
            }

            if (versions.Any(v => !_qualityProfileService.Exists(v.QualityProfileId)))
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "A default version's quality profile doesn't exist");
            }

            _configService.DefaultSeriesVersions = versions.Any() ? versions.ToJson() : string.Empty;
        }

        public void Handle(SeriesAddedEvent message)
        {
            Apply(message.Series);
        }

        public void Handle(SeriesImportedEvent message)
        {
            if (Get().Empty())
            {
                return;
            }

            foreach (var series in _seriesService.GetSeries(message.SeriesIds))
            {
                Apply(series);
            }
        }

        private void Apply(Series series)
        {
            foreach (var version in Get())
            {
                try
                {
                    _versionSlotService.Add(new SeriesVersionSlot
                    {
                        SeriesId = series.Id,
                        Label = version.Label,
                        QualityProfileId = version.QualityProfileId,
                        Monitored = series.Monitored
                    });
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to give '{0}' the default version '{1}'", series.Title, version.Label);
                }
            }
        }
    }
}
