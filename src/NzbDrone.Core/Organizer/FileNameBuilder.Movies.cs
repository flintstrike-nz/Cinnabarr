using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Organizer
{
    public interface IBuildMovieFileNames
    {
        string BuildFileName(Movie movie, MovieFile movieFile, NamingConfig namingConfig = null, List<CustomFormat> customFormats = null);
        string BuildFilePath(Movie movie, string fileName, string extension);
        string GetMovieFolder(Movie movie, NamingConfig namingConfig = null);
    }

    // The movie side of the file name builder: the same token engine as the series side, with Radarr's movie tokens.
    public partial class FileNameBuilder
    {
        // Unlike the series pattern this one also matches the {imdb-{ImdbId}} and {edition-{Edition Tags}} forms.
        private static readonly Regex MovieTokenRegex = new Regex(@"(?<tag>\{(?<prefix>[-{ ._\[(]*)(?:imdb(?:id)?-|edition-))?\{(?<prefix>[-{ ._\[(]*)(?<token>(?:[a-z0-9]+)(?:(?<separator>[- ._]+)(?:[a-z0-9]+))?)(?::(?<customFormat>[ ,a-z0-9|+-]+(?<![- ])))?(?<suffix>[-} ._)\]]*)\}",
                                                                  RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static readonly Regex ReleaseYearRegex = new Regex(@"\{[-{ ._\[(]*Release[- ._]Year[-} ._)\]]*\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex MovieTitleRegex = new Regex(@"(?<token>\{(?:Movie)(?<separator>[- ._])(?:Clean)?(?:OriginalTitle|Title(?:The)?)(?::(?<customFormat>[a-z0-9|-]+))?\})",
                                                                 RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public string BuildFileName(Movie movie, MovieFile movieFile, NamingConfig namingConfig = null, List<CustomFormat> customFormats = null)
        {
            namingConfig ??= _namingConfigService.GetConfig();

            if (!namingConfig.RenameMovies)
            {
                return GetOriginalTitle(movieFile, false);
            }

            if (namingConfig.StandardMovieFormat.IsNullOrWhiteSpace())
            {
                throw new NamingFormatException("Standard movie format cannot be empty");
            }

            var pattern = namingConfig.StandardMovieFormat;

            var tokenHandlers = new Dictionary<string, Func<TokenMatch, string>>(FileNameBuilderTokenEqualityComparer.Instance);
            var multipleTokens = MovieTokenRegex.Matches(pattern).Count > 1;

            UpdateMediaInfoIfNeeded(pattern, movieFile, movie);

            AddMovieTokens(tokenHandlers, movie);
            AddMovieReleaseDateTokens(tokenHandlers, movie.Year);
            AddMovieIdTokens(tokenHandlers, movie);
            AddQualityTokens(tokenHandlers, movie, movieFile);
            AddMediaInfoTokens(tokenHandlers, movieFile);
            AddMovieFileTokens(tokenHandlers, movieFile, multipleTokens);
            AddEditionTagsTokens(tokenHandlers, movieFile);
            AddCustomFormats(tokenHandlers, movie, movieFile, customFormats);

            var components = new List<string>();

            foreach (var splitPattern in pattern.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var component = ReplaceMovieTokens(splitPattern, tokenHandlers, namingConfig).Trim();

                component = FileNameCleanupRegex.Replace(component, match => match.Captures[0].Value[0].ToString());
                component = TrimSeparatorsRegex.Replace(component, string.Empty);
                component = component.Replace("{ellipsis}", "...");
                component = ReplaceReservedDeviceNames(component);

                if (component.IsNotNullOrWhiteSpace())
                {
                    components.Add(component);
                }
            }

            return Path.Combine(components.ToArray());
        }

        public string BuildFilePath(Movie movie, string fileName, string extension)
        {
            Ensure.That(extension, () => extension).IsNotNullOrWhiteSpace();

            return Path.Combine(movie.Path, fileName + extension);
        }

        public string GetMovieFolder(Movie movie, NamingConfig namingConfig = null)
        {
            namingConfig ??= _namingConfigService.GetConfig();

            var tokenHandlers = new Dictionary<string, Func<TokenMatch, string>>(FileNameBuilderTokenEqualityComparer.Instance);

            AddMovieTokens(tokenHandlers, movie);
            AddMovieReleaseDateTokens(tokenHandlers, movie.Year);
            AddMovieIdTokens(tokenHandlers, movie);

            var components = new List<string>();

            foreach (var splitPattern in namingConfig.MovieFolderFormat.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var component = ReplaceMovieTokens(splitPattern, tokenHandlers, namingConfig);
                component = CleanFolderName(component);
                component = component.Replace("{ellipsis}", "...");
                component = ReplaceReservedDeviceNames(component);

                if (component.IsNotNullOrWhiteSpace())
                {
                    components.Add(component);
                }
            }

            return Path.Combine(components.ToArray());
        }

        private void AddMovieTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, Movie movie)
        {
            var metadata = movie.MovieMetadata.Value;

            tokenHandlers["{Movie Title}"] = m => Truncate(GetLanguageTitle(movie, m.CustomFormat), m.CustomFormat);
            tokenHandlers["{Movie CleanTitle}"] = m => Truncate(CleanTitle(GetLanguageTitle(movie, m.CustomFormat)), m.CustomFormat);
            tokenHandlers["{Movie TitleThe}"] = m => Truncate(TitleThe(movie.Title), m.CustomFormat);
            tokenHandlers["{Movie CleanTitleThe}"] = m => Truncate(CleanTitleThe(movie.Title), m.CustomFormat);
            tokenHandlers["{Movie TitleFirstCharacter}"] = m => TitleFirstCharacter(TitleThe(GetLanguageTitle(movie, m.CustomFormat)));
            tokenHandlers["{Movie OriginalTitle}"] = m => Truncate(metadata.OriginalTitle, m.CustomFormat) ?? string.Empty;
            tokenHandlers["{Movie CleanOriginalTitle}"] = m => Truncate(CleanTitle(metadata.OriginalTitle ?? string.Empty), m.CustomFormat);

            tokenHandlers["{Movie Certification}"] = m => metadata.Certification ?? string.Empty;
            tokenHandlers["{Movie Collection}"] = m => Truncate(metadata.CollectionTitle, m.CustomFormat) ?? string.Empty;
            tokenHandlers["{Movie CollectionThe}"] = m => Truncate(TitleThe(metadata.CollectionTitle), m.CustomFormat) ?? string.Empty;
            tokenHandlers["{Movie CleanCollectionThe}"] = m => Truncate(CleanTitleThe(metadata.CollectionTitle), m.CustomFormat) ?? string.Empty;
        }

        private string GetLanguageTitle(Movie movie, string isoCodes)
        {
            if (isoCodes.IsNotNullOrWhiteSpace())
            {
                foreach (var isoCode in isoCodes.Split('|'))
                {
                    var language = IsoLanguages.Find(isoCode.ToLower())?.Language;

                    if (language == null)
                    {
                        continue;
                    }

                    var translations = movie.MovieMetadata.Value.Translations;

                    if (translations == null || !translations.Any())
                    {
                        translations = _movieTranslationService.GetAllTranslationsForMovieMetadata(movie.MovieMetadataId);
                    }

                    return translations.FirstOrDefault(t => t.Language == language)?.Title ?? movie.Title;
                }
            }

            return movie.Title;
        }

        private void AddEditionTagsTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, MovieFile movieFile)
        {
            if (movieFile.Edition.IsNotNullOrWhiteSpace())
            {
                tokenHandlers["{Edition Tags}"] = m => Truncate(GetEditionToken(movieFile), m.CustomFormat);
            }
        }

        private static string GetEditionToken(MovieFile movieFile)
        {
            var edition = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(movieFile.Edition.ToLowerInvariant());

            edition = Regex.Replace(edition, @"((?:\b|_)\d{1,3}(?:st|th|rd|nd|mm)(?:\b|_))", match => match.Groups[1].Value.ToLowerInvariant(), RegexOptions.IgnoreCase);
            edition = Regex.Replace(edition, @"((?:\b|_)(?:IMAX|3D|SDR|HDR|DV)(?:\b|_))", match => match.Groups[1].Value.ToUpperInvariant(), RegexOptions.IgnoreCase);

            return edition;
        }

        private static void AddMovieReleaseDateTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, int releaseYear)
        {
            tokenHandlers["{Release Year}"] = m => releaseYear == 0 ? string.Empty : releaseYear.ToString();
        }

        private static void AddMovieIdTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, Movie movie)
        {
            tokenHandlers["{ImdbId}"] = m => movie.MovieMetadata.Value.ImdbId ?? string.Empty;
            tokenHandlers["{TmdbId}"] = m => movie.MovieMetadata.Value.TmdbId.ToString();
        }

        private void AddMovieFileTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, MovieFile movieFile, bool multipleTokens)
        {
            tokenHandlers["{Original Title}"] = m => GetOriginalTitle(movieFile, multipleTokens);
            tokenHandlers["{Original Filename}"] = m => GetOriginalFileName(movieFile, multipleTokens);
            tokenHandlers["{Release Group}"] = m => movieFile.ReleaseGroup.IsNullOrWhiteSpace() ? m.DefaultValue("Embarr") : Truncate(movieFile.ReleaseGroup, m.CustomFormat);
        }

        private void AddQualityTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, Movie movie, MovieFile movieFile)
        {
            if (movieFile?.Quality?.Quality == null)
            {
                tokenHandlers["{Quality Full}"] = m => string.Empty;
                tokenHandlers["{Quality Title}"] = m => string.Empty;
                tokenHandlers["{Quality Proper}"] = m => string.Empty;
                tokenHandlers["{Quality Real}"] = m => string.Empty;
                return;
            }

            var qualityTitle = _qualityDefinitionService.Get(movieFile.Quality.Quality).Title;
            var qualityProper = movieFile.Quality.Revision.Version > 1 ? "Proper" : string.Empty;
            var qualityReal = movieFile.Quality.Revision.Real > 0 ? "REAL" : string.Empty;

            tokenHandlers["{Quality Full}"] = m => string.Format("{0} {1} {2}", qualityTitle, qualityProper, qualityReal);
            tokenHandlers["{Quality Title}"] = m => qualityTitle;
            tokenHandlers["{Quality Proper}"] = m => qualityProper;
            tokenHandlers["{Quality Real}"] = m => qualityReal;
        }

        // Media information is read again when the pattern uses a token that older information doesn't have.
        private void UpdateMediaInfoIfNeeded(string pattern, MovieFile movieFile, Movie movie)
        {
            if (movie.Path.IsNullOrWhiteSpace())
            {
                return;
            }

            var schemaRevision = movieFile.MediaInfo?.SchemaRevision ?? 0;

            var shouldUpdateMediaInfo = MovieTokenRegex.Matches(pattern).Cast<Match>()
                .Select(m => MinimumMediaInfoSchemaRevisions.GetValueOrDefault(m.Value, -1))
                .Any(r => schemaRevision < r);

            if (shouldUpdateMediaInfo)
            {
                _mediaInfoUpdater.Update(movieFile, movie);
            }
        }

        private void AddMediaInfoTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, MovieFile movieFile)
        {
            if (movieFile.MediaInfo == null)
            {
                _logger.Trace("Media info is unavailable for {0}", movieFile);

                return;
            }

            var sceneName = movieFile.GetSceneOrFileName();

            var videoCodec = MediaInfoFormatter.FormatVideoCodec(movieFile.MediaInfo, sceneName) ?? string.Empty;
            var audioCodec = MediaInfoFormatter.FormatAudioCodec(movieFile.MediaInfo.PrimaryAudioStream, sceneName) ?? string.Empty;
            var audioChannels = MediaInfoFormatter.FormatAudioChannels(movieFile.MediaInfo.PrimaryAudioStream);
            var audioLanguages = movieFile.MediaInfo.AudioStreams?.Select(l => l.Language).ToList() ?? [];
            var subtitles = movieFile.MediaInfo.SubtitleStreams?.Select(l => l.Language).ToList() ?? [];

            var videoBitDepth = movieFile.MediaInfo.VideoBitDepth > 0 ? movieFile.MediaInfo.VideoBitDepth.ToString() : 8.ToString();
            var audioChannelsFormatted = audioChannels > 0 ? audioChannels.ToString("F1", CultureInfo.InvariantCulture) : string.Empty;

            tokenHandlers["{MediaInfo Video}"] = m => videoCodec;
            tokenHandlers["{MediaInfo VideoCodec}"] = m => videoCodec;
            tokenHandlers["{MediaInfo VideoBitDepth}"] = m => videoBitDepth;

            tokenHandlers["{MediaInfo Audio}"] = m => audioCodec;
            tokenHandlers["{MediaInfo AudioCodec}"] = m => audioCodec;
            tokenHandlers["{MediaInfo AudioChannels}"] = m => audioChannelsFormatted;
            tokenHandlers["{MediaInfo AudioLanguages}"] = m => GetLanguagesToken(audioLanguages, m.CustomFormat, true, true);
            tokenHandlers["{MediaInfo AudioLanguagesAll}"] = m => GetLanguagesToken(audioLanguages, m.CustomFormat, false, true);

            tokenHandlers["{MediaInfo SubtitleLanguages}"] = m => GetLanguagesToken(subtitles, m.CustomFormat, false, true);
            tokenHandlers["{MediaInfo SubtitleLanguagesAll}"] = m => GetLanguagesToken(subtitles, m.CustomFormat, false, true);

            tokenHandlers["{MediaInfo Simple}"] = m => $"{videoCodec} {audioCodec}";
            tokenHandlers["{MediaInfo Full}"] = m => $"{videoCodec} {audioCodec}{GetLanguagesToken(audioLanguages, m.CustomFormat, true, true)} {GetLanguagesToken(subtitles, m.CustomFormat, false, true)}";

            tokenHandlers[MediaInfoVideoDynamicRangeToken] = m => MediaInfoFormatter.FormatVideoDynamicRange(movieFile.MediaInfo);
            tokenHandlers[MediaInfoVideoDynamicRangeTypeToken] = m => MediaInfoFormatter.FormatVideoDynamicRangeType(movieFile.MediaInfo);
        }

        private void AddCustomFormats(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, Movie movie, MovieFile movieFile, List<CustomFormat> customFormats = null)
        {
            if (customFormats == null)
            {
                movieFile.Movie = movie;
                customFormats = _formatCalculator.ParseCustomFormat(movieFile);
            }

            tokenHandlers["{Custom Formats}"] = m => GetCustomFormatsToken(customFormats, m.CustomFormat);
            tokenHandlers["{Custom Format}"] = m =>
            {
                if (m.CustomFormat.IsNullOrWhiteSpace())
                {
                    return string.Empty;
                }

                return customFormats.FirstOrDefault(x => x.IncludeCustomFormatWhenRenaming && x.Name == m.CustomFormat)?.ToString() ?? string.Empty;
            };
        }

        private static string GetOriginalTitle(MovieFile movieFile, bool multipleTokens)
        {
            if (movieFile.SceneName.IsNullOrWhiteSpace())
            {
                return CleanFileName(GetOriginalFileName(movieFile, multipleTokens));
            }

            return CleanFileName(movieFile.SceneName);
        }

        private static string GetOriginalFileName(MovieFile movieFile, bool multipleTokens)
        {
            if (multipleTokens)
            {
                return string.Empty;
            }

            if (movieFile.RelativePath.IsNullOrWhiteSpace())
            {
                return Path.GetFileNameWithoutExtension(movieFile.Path);
            }

            return Path.GetFileNameWithoutExtension(movieFile.RelativePath);
        }

        private string ReplaceMovieTokens(string pattern, Dictionary<string, Func<TokenMatch, string>> tokenHandlers, NamingConfig namingConfig)
        {
            return MovieTokenRegex.Replace(pattern, match => ReplaceMovieToken(match, tokenHandlers, namingConfig));
        }

        private static string ReplaceMovieToken(Match match, Dictionary<string, Func<TokenMatch, string>> tokenHandlers, NamingConfig namingConfig)
        {
            var tokenMatch = new TokenMatch
            {
                RegexMatch = match,
                Tag = match.Groups["tag"].Value,
                Prefix = match.Groups["prefix"].Value,
                Separator = match.Groups["separator"].Value,
                Suffix = match.Groups["suffix"].Value,
                Token = match.Groups["token"].Value,
                CustomFormat = match.Groups["customFormat"].Value
            };

            if (tokenMatch.CustomFormat.IsNullOrWhiteSpace())
            {
                tokenMatch.CustomFormat = null;
            }

            var tokenHandler = tokenHandlers.GetValueOrDefault(tokenMatch.Token, m => string.Empty);

            var replacementText = tokenHandler(tokenMatch).Trim();

            if (tokenMatch.Token.All(t => !char.IsLetter(t) || char.IsLower(t)))
            {
                replacementText = replacementText.ToLower();
            }
            else if (tokenMatch.Token.All(t => !char.IsLetter(t) || char.IsUpper(t)))
            {
                replacementText = replacementText.ToUpper();
            }

            if (!tokenMatch.Separator.IsNullOrWhiteSpace())
            {
                replacementText = replacementText.Replace(" ", tokenMatch.Separator);
            }

            replacementText = CleanFileName(replacementText, namingConfig);

            if (!replacementText.IsNullOrWhiteSpace())
            {
                replacementText = tokenMatch.Tag + tokenMatch.Prefix + replacementText + tokenMatch.Suffix;
            }

            return replacementText;
        }
    }
}
