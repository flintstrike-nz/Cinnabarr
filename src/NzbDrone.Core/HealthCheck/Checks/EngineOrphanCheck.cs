using System.Collections.Generic;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(EngineOrphanScanCompletedEvent))]
    public class EngineOrphanCheck : HealthCheckBase
    {
        private readonly IEngineOrphanService _engineOrphanService;

        public EngineOrphanCheck(IEngineOrphanService engineOrphanService, ILocalizationService localizationService)
            : base(localizationService)
        {
            _engineOrphanService = engineOrphanService;
        }

        public override HealthCheck Check()
        {
            var count = _engineOrphanService.AwaitingReview;

            if (count == 0)
            {
                return new HealthCheck(GetType());
            }

            return new HealthCheck(GetType(),
                HealthCheckResult.Notice,
                HealthCheckReason.EngineOrphansAwaitingReview,
                _localizationService.GetLocalizedString("EngineOrphansHealthCheckMessage", new Dictionary<string, object> { { "count", count } }));
        }
    }
}
