using FluentValidation;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Engine;
using Sonarr.Http;

namespace Sonarr.Api.V5.Settings;

[V5ApiController("settings/downloadclient")]
public class DownloadClientSettingsController : SettingsController<DownloadClientSettingsResource>
{
    public DownloadClientSettingsController(IConfigFileProvider configFileProvider, IConfigService configService)
        : base(configFileProvider, configService)
    {
        SharedValidator.RuleFor(c => c.EngineRaceMaxSources).InclusiveBetween(1, ConfigService.MaxRaceSources).When(c => c.EngineRaceMaxSources.HasValue);
        SharedValidator.RuleFor(c => c.EngineOrphanGraceDays).InclusiveBetween(1, EngineOrphanService.MaxGraceDays).When(c => c.EngineOrphanGraceDays.HasValue);
    }

    protected override DownloadClientSettingsResource ToResource(IConfigFileProvider configFile, IConfigService model)
    {
        return DownloadClientSettingsResourceMapper.ToResource(model);
    }
}
