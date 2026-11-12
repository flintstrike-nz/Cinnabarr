using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Movies.Versions
{
    // A version every new movie gets, such as "4K" with the Ultra-HD profile.
    public class DefaultVersion
    {
        public string Label { get; set; }
        public int QualityProfileId { get; set; }
    }

    public interface IDefaultVersionService
    {
        List<DefaultVersion> Get();
        void Set(List<DefaultVersion> versions);
    }

    /// <summary>Roadmap Phase 5 "default version slots": the versions a movie gets when it is added (or imported).</summary>
    public class DefaultVersionService : IDefaultVersionService, IHandle<MovieAddedEvent>, IHandle<MoviesImportedEvent>
    {
        private readonly IConfigService _configService;
        private readonly IVersionSlotService _versionSlotService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly Logger _logger;

        public DefaultVersionService(IConfigService configService, IVersionSlotService versionSlotService, IQualityProfileService qualityProfileService, Logger logger)
        {
            _configService = configService;
            _versionSlotService = versionSlotService;
            _qualityProfileService = qualityProfileService;
            _logger = logger;
        }

        public List<DefaultVersion> Get()
        {
            var json = _configService.DefaultMovieVersions;

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
                _logger.Warn(ex, "The default movie versions couldn't be read and are ignored");

                return new List<DefaultVersion>();
            }
        }

        public void Set(List<DefaultVersion> versions)
        {
            versions = (versions ?? new List<DefaultVersion>()).Select(v => new DefaultVersion { Label = v.Label?.Trim(), QualityProfileId = v.QualityProfileId }).ToList();

            if (versions.Count >= VersionSlotService.MaxSlotsPerMovie)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "At most {0} default versions can be set", VersionSlotService.MaxSlotsPerMovie - 1);
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

            _configService.DefaultMovieVersions = versions.Any() ? versions.ToJson() : string.Empty;
        }

        public void Handle(MovieAddedEvent message)
        {
            Apply(message.Movie);
        }

        public void Handle(MoviesImportedEvent message)
        {
            foreach (var movie in message.Movies)
            {
                Apply(movie);
            }
        }

        private void Apply(Movie movie)
        {
            foreach (var version in Get())
            {
                try
                {
                    _versionSlotService.Add(new VersionSlot
                    {
                        MovieId = movie.Id,
                        Label = version.Label,
                        QualityProfileId = version.QualityProfileId,
                        Monitored = movie.Monitored
                    });
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to give '{0}' the default version '{1}'", movie.Title, version.Label);
                }
            }
        }
    }
}
