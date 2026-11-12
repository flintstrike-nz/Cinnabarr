using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Organizer
{
    public class NamingConfig : ModelBase
    {
        // Jellyfin's conventions (roadmap §4.11); the other presets are in NamingPresets.
        public static NamingConfig Default => NamingPresets.Get(NamingPreset.Jellyfin);

        public bool RenameEpisodes { get; set; }
        public bool ReplaceIllegalCharacters { get; set; }
        public ColonReplacementFormat ColonReplacementFormat { get; set; }
        public string CustomColonReplacementFormat { get; set; }
        public MultiEpisodeStyle MultiEpisodeStyle { get; set; }
        public string StandardEpisodeFormat { get; set; }
        public string DailyEpisodeFormat { get; set; }
        public string AnimeEpisodeFormat { get; set; }
        public string SeriesFolderFormat { get; set; }
        public string SeasonFolderFormat { get; set; }
        public string SpecialsFolderFormat { get; set; }
        public bool RenameMovies { get; set; }
        public string StandardMovieFormat { get; set; }
        public string MovieFolderFormat { get; set; }
    }
}
