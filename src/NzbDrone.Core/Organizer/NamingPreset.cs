namespace NzbDrone.Core.Organizer
{
    // The media servers whose naming conventions Cinnabarr can follow. Jellyfin is the default.
    public enum NamingPreset
    {
        Jellyfin = 0,
        Emby = 1,
        Plex = 2
    }

    public static class NamingPresets
    {
        // The formats a preset sets. Everything else (replacing illegal characters, colons, multi-episode style) keeps
        // the defaults, and renaming is on so the library has consistent names.
        public static NamingConfig Get(NamingPreset preset)
        {
            // The media server finds a title from the provider id in its folder name: Jellyfin reads [tvdbid-123],
            // Emby [tvdbid=123] and Plex {tvdb-123}.
            var (seriesId, movieId) = preset switch
            {
                NamingPreset.Emby => ("[tvdbid={TvdbId}]", "[tmdbid={TmdbId}]"),
                NamingPreset.Plex => ("{tvdb-{TvdbId}}", "{tmdb-{TmdbId}}"),
                _ => ("[tvdbid-{TvdbId}]", "[tmdbid-{TmdbId}]")
            };

            return new NamingConfig
            {
                RenameEpisodes = true,
                ReplaceIllegalCharacters = true,
                ColonReplacementFormat = ColonReplacementFormat.Smart,
                CustomColonReplacementFormat = string.Empty,
                MultiEpisodeStyle = MultiEpisodeStyle.PrefixedRange,
                StandardEpisodeFormat = "{Series TitleYear} - S{season:00}E{episode:00} - {Episode Title}",
                DailyEpisodeFormat = "{Series TitleYear} - {Air-Date} - {Episode Title}",
                AnimeEpisodeFormat = "{Series TitleYear} - S{season:00}E{episode:00} - {Episode Title}",
                SeriesFolderFormat = "{Series TitleYear} " + seriesId,
                SeasonFolderFormat = "Season {season:00}",
                SpecialsFolderFormat = "Specials",
                RenameMovies = true,
                StandardMovieFormat = "{Movie Title} ({Release Year}){ - Version}",
                MovieFolderFormat = "{Movie Title} ({Release Year}) " + movieId
            };
        }
    }
}
