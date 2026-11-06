using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Movies.Specifications
{
    public class MovieIndexerTagSpecification : IMovieDecisionEngineSpecification
    {
        private readonly Logger _logger;
        private readonly IIndexerFactory _indexerFactory;

        public MovieIndexerTagSpecification(Logger logger, IIndexerFactory indexerFactory)
        {
            _logger = logger;
            _indexerFactory = indexerFactory;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual DownloadSpecDecision IsSatisfiedBy(RemoteMovie subject, MovieReleaseDecisionInformation information)
        {
            if (subject.Release == null || subject.Movie?.Tags == null || subject.Release.IndexerId == 0)
            {
                return DownloadSpecDecision.Accept();
            }

            IndexerDefinition indexer;

            try
            {
                indexer = _indexerFactory.Get(subject.Release.IndexerId);
            }
            catch (ModelNotFoundException)
            {
                _logger.Debug("Indexer with id {0} does not exist, skipping indexer tags check", subject.Release.IndexerId);
                return DownloadSpecDecision.Accept();
            }

            // If the indexer has tags, at least one of them has to be on the movie.
            var indexerTags = indexer.Tags;

            if (indexerTags.Any() && indexerTags.Intersect(subject.Movie.Tags).Empty())
            {
                _logger.Debug("Indexer {0} has tags. None of these are present on movie {1}. Rejecting", subject.Release.Indexer, subject.Movie);

                return DownloadSpecDecision.Reject(DownloadRejectionReason.NoMatchingTag, "Movie tags do not match any of the indexer tags");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
