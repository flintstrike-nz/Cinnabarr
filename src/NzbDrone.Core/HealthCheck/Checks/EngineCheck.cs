using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    public class EngineCheck : HealthCheckBase
    {
        private readonly IEngineSupervisor _engineSupervisor;

        public EngineCheck(IEngineSupervisor engineSupervisor, ILocalizationService localizationService)
            : base(localizationService)
        {
            _engineSupervisor = engineSupervisor;
        }

        public override HealthCheck Check()
        {
            switch (_engineSupervisor.Status)
            {
                case EngineStatus.NotFound:
                    return new HealthCheck(GetType(),
                        HealthCheckResult.Warning,
                        HealthCheckReason.EngineNotFound,
                        _localizationService.GetLocalizedString("EngineNotFoundHealthCheckMessage"));

                case EngineStatus.Failed:
                    return new HealthCheck(GetType(),
                        HealthCheckResult.Error,
                        HealthCheckReason.EngineNotRunning,
                        _localizationService.GetLocalizedString("EngineNotRunningHealthCheckMessage"));

                default:
                    return new HealthCheck(GetType());
            }
        }
    }
}
