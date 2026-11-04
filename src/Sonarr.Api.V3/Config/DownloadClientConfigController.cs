using FluentValidation;
using NzbDrone.Core.Configuration;
using Sonarr.Http;

namespace Sonarr.Api.V3.Config
{
    [V3ApiController("config/downloadclient")]
    public class DownloadClientConfigController : ConfigController<DownloadClientConfigResource>
    {
        public DownloadClientConfigController(IConfigService configService)
            : base(configService)
        {
            SharedValidator.RuleFor(c => c.EngineOrphanGraceDays).GreaterThanOrEqualTo(1).When(c => c.EngineOrphanGraceDays.HasValue);
        }

        protected override DownloadClientConfigResource ToResource(IConfigService model)
        {
            return DownloadClientConfigResourceMapper.ToResource(model);
        }
    }
}
