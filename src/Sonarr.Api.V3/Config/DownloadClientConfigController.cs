using FluentValidation;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Engine;
using Sonarr.Http;

namespace Sonarr.Api.V3.Config
{
    [V3ApiController("config/downloadclient")]
    public class DownloadClientConfigController : ConfigController<DownloadClientConfigResource>
    {
        public DownloadClientConfigController(IConfigService configService)
            : base(configService)
        {
            SharedValidator.RuleFor(c => c.EngineOrphanGraceDays).InclusiveBetween(1, EngineOrphanService.MaxGraceDays).When(c => c.EngineOrphanGraceDays.HasValue);
        }

        protected override DownloadClientConfigResource ToResource(IConfigService model)
        {
            return DownloadClientConfigResourceMapper.ToResource(model);
        }
    }
}
