using NzbDrone.Core.Movies;

namespace NzbDrone.Core.DecisionEngine.Movies
{
    public class MovieReleaseDecisionInformation
    {
        public bool PushedRelease { get; set; }

        // The movie a search was made for, when the release comes from a search rather than from RSS.
        public Movie SearchedMovie { get; set; }

        public MovieReleaseDecisionInformation()
        {
        }

        public MovieReleaseDecisionInformation(bool pushedRelease, Movie searchedMovie)
        {
            PushedRelease = pushedRelease;
            SearchedMovie = searchedMovie;
        }
    }
}
