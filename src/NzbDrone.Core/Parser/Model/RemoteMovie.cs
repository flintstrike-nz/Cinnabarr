using System.Collections.Generic;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Parser.Model
{
    public class RemoteMovie : RemoteMedia
    {
        public ParsedMovieInfo ParsedMovieInfo { get; set; }
        public MovieMatchType MovieMatchType { get; set; }
        public Movie Movie { get; set; }
        public bool MovieRequested { get; set; }

        public override HashSet<int> Tags => Movie?.Tags ?? new HashSet<int>();

        public override bool IsRecent => Movie?.MovieMetadata?.Value?.IsRecentMovie ?? false;
    }
}
