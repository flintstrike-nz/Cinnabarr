namespace NzbDrone.Core.ImportLists.Movies
{
    // A movie offered by an import list; the sync step turns it into a library movie.
    public class ImportListMovieInfo
    {
        public int ImportListId { get; set; }
        public string ImportList { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }

        public override string ToString()
        {
            return $"{Title} (tmdb:{TmdbId} imdb:{ImdbId})";
        }
    }
}
