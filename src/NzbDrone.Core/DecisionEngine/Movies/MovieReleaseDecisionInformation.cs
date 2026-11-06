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

        // The search was asked for by the user rather than run on a schedule.
        public bool UserInvokedSearch { get; set; }

        public MovieReleaseDecisionInformation(bool pushedRelease, Movie searchedMovie, bool userInvokedSearch = false)
        {
            PushedRelease = pushedRelease;
            SearchedMovie = searchedMovie;
            UserInvokedSearch = userInvokedSearch;
        }
    }
}
