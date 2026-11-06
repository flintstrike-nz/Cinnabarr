namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public class MovieSearchCriteria : SearchCriteriaBase
    {
        public override string ToString()
        {
            return $"[{Movie.Title} ({Movie.Year})]";
        }
    }
}
