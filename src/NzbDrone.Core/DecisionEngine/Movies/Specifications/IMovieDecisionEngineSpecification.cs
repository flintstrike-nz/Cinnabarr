using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Movies.Specifications
{
    public interface IMovieDecisionEngineSpecification
    {
        RejectionType Type { get; }

        SpecificationPriority Priority { get; }

        DownloadSpecDecision IsSatisfiedBy(RemoteMovie subject, MovieReleaseDecisionInformation information);
    }
}
