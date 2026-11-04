using FluentValidation;
using NzbDrone.Core.Configuration;
using Sonarr.Http;

namespace Sonarr.Api.V5.Settings;

[V5ApiController("settings/downloadclient")]
public class DownloadClientSettingsController : SettingsController<DownloadClientSettingsResource>
{
    public DownloadClientSettingsController(IConfigFileProvider configFileProvider, IConfigService configService)
        : base(configFileProvider, configService)
    {
        SharedValidator.RuleFor(c => c.EngineOrphanGraceDays).GreaterThanOrEqualTo(1).When(c => c.EngineOrphanGraceDays.HasValue);
    }

    protected override DownloadClientSettingsResource ToResource(IConfigFileProvider configFile, IConfigService model)
    {
        return DownloadClientSettingsResourceMapper.ToResource(model);
    }
}
