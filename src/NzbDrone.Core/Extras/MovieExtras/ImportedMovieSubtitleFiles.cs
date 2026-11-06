using System.Collections.Generic;

namespace NzbDrone.Core.Extras.MovieExtras
{
    public class MovieImportedSubtitleFiles
    {
        public List<string> SourceFiles { get; set; }
        public List<MovieExtraFile> SubtitleFiles { get; set; }

        public MovieImportedSubtitleFiles()
        {
            SourceFiles = new List<string>();
            SubtitleFiles = new List<MovieExtraFile>();
        }
    }
}
