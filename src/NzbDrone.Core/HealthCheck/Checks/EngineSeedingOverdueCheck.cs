using System.Collections.Generic;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(EngineSeedingProcessedEvent))]
    public class EngineSeedingOverdueCheck : HealthCheckBase
    {
        private readonly IEngineSeedingService _engineSeedingService;

        public EngineSeedingOverdueCheck(IEngineSeedingService engineSeedingService, ILocalizationService localizationService)
            : base(localizationService)
        {
            _engineSeedingService = engineSeedingService;
        }

        public override HealthCheck Check()
        {
            var overdue = _engineSeedingService.OverdueTorrents;

            if (overdue.Count == 0)
            {
                return new HealthCheck(GetType());
            }

            return new HealthCheck(GetType(),
                HealthCheckResult.Warning,
                HealthCheckReason.EngineSeedingOverdue,
                _localizationService.GetLocalizedString("EngineSeedingOverdueHealthCheckMessage", new Dictionary<string, object> { { "count", overdue.Count }, { "names", overdue.Join(", ") } }));
        }
    }
}
