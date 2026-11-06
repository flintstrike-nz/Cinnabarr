using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.History;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.CustomFormats
{
    public interface ICustomFormatCalculationService
    {
        List<CustomFormat> ParseCustomFormat(RemoteEpisode remoteEpisode, long size);
        List<CustomFormat> ParseCustomFormat(RemoteMovie remoteMovie, long size);
        List<CustomFormat> ParseCustomFormat(MovieFile movieFile);
        List<CustomFormat> ParseCustomFormat(MovieHistory history, Movie movie);
        List<CustomFormat> ParseCustomFormat(LocalMovie localMovie, string fileName);
        List<CustomFormat> ParseCustomFormat(EpisodeFile episodeFile, Series series);
        List<CustomFormat> ParseCustomFormat(EpisodeFile episodeFile);
        List<CustomFormat> ParseCustomFormat(Blocklist blocklist, Series series);
        List<CustomFormat> ParseCustomFormat(EpisodeHistory history, Series series);
        List<CustomFormat> ParseCustomFormat(LocalEpisode localEpisode, string fileName);
    }

    public class CustomFormatCalculationService : ICustomFormatCalculationService
    {
        private readonly ICustomFormatService _formatService;
        private readonly Logger _logger;

        public CustomFormatCalculationService(ICustomFormatService formatService, Logger logger)
        {
            _formatService = formatService;
            _logger = logger;
        }

        public List<CustomFormat> ParseCustomFormat(RemoteEpisode remoteEpisode, long size)
        {
            var input = new CustomFormatInput
            {
                EpisodeInfo = remoteEpisode.ParsedEpisodeInfo,
                Series = remoteEpisode.Series,
                Size = size,
                Languages = remoteEpisode.Languages,
                IndexerFlags = remoteEpisode.Release?.IndexerFlags ?? 0,
                ReleaseType = remoteEpisode.ParsedEpisodeInfo.ReleaseType
            };

            return ParseCustomFormat(input);
        }

        public List<CustomFormat> ParseCustomFormat(RemoteMovie remoteMovie, long size)
        {
            var input = new CustomFormatInput
            {
                MovieInfo = remoteMovie.ParsedMovieInfo,
                Movie = remoteMovie.Movie,
                Size = size,
                Languages = remoteMovie.Languages,
                IndexerFlags = remoteMovie.Release?.IndexerFlags ?? 0
            };

            return ParseCustomFormat(input);
        }

        public List<CustomFormat> ParseCustomFormat(MovieFile movieFile)
        {
            var movie = movieFile.Movie;

            var info = new ParsedMovieInfo
            {
                MovieTitles = new List<string> { movie?.Title ?? string.Empty },
                SimpleReleaseTitle = movieFile.GetSceneOrFileName(),
                Quality = movieFile.Quality,
                Languages = movieFile.Languages ?? new List<Language>(),
                ReleaseGroup = movieFile.ReleaseGroup,
                Edition = movieFile.Edition,
                Year = movie?.Year ?? 0
            };

            var input = new CustomFormatInput
            {
                MovieInfo = info,
                Movie = movie,
                Size = movieFile.Size,
                Languages = info.Languages,
                IndexerFlags = movieFile.IndexerFlags,
                Filename = System.IO.Path.GetFileName(movieFile.RelativePath)
            };

            return ParseCustomFormat(input);
        }

        public List<CustomFormat> ParseCustomFormat(MovieHistory history, Movie movie)
        {
            var parsed = MovieParser.ParseMovieTitle(history.SourceTitle);

            long.TryParse(history.Data.GetValueOrDefault("size"), out var size);
            Enum.TryParse(history.Data.GetValueOrDefault("indexerFlags"), true, out IndexerFlags indexerFlags);

            var movieInfo = new ParsedMovieInfo
            {
                MovieTitles = new List<string> { movie.Title },
                SimpleReleaseTitle = parsed?.SimpleReleaseTitle ?? history.SourceTitle,
                ReleaseTitle = parsed?.ReleaseTitle ?? history.SourceTitle,
                Year = movie.Year,
                Edition = parsed?.Edition,
                Quality = history.Quality,
                Languages = history.Languages ?? new List<Language>(),
                ReleaseGroup = parsed?.ReleaseGroup
            };

            var input = new CustomFormatInput
            {
                MovieInfo = movieInfo,
                Movie = movie,
                Size = size,
                Languages = movieInfo.Languages,
                IndexerFlags = indexerFlags
            };

            return ParseCustomFormat(input);
        }

        public List<CustomFormat> ParseCustomFormat(LocalMovie localMovie, string fileName)
        {
            var movieInfo = new ParsedMovieInfo
            {
                MovieTitles = new List<string> { localMovie.Movie.Title },
                SimpleReleaseTitle = localMovie.SceneName.IsNotNullOrWhiteSpace() ? localMovie.SceneName.SimplifyReleaseTitle() : Path.GetFileName(localMovie.Path).SimplifyReleaseTitle(),
                ReleaseTitle = localMovie.SceneName,
                Year = localMovie.Movie.Year,
                Quality = localMovie.Quality,
                Edition = localMovie.Edition,
                Languages = localMovie.Languages,
                ReleaseGroup = localMovie.ReleaseGroup
            };

            var input = new CustomFormatInput
            {
                MovieInfo = movieInfo,
                Movie = localMovie.Movie,
                Size = localMovie.Size,
                Languages = localMovie.Languages,
                IndexerFlags = localMovie.IndexerFlags,
                Filename = fileName
            };

            return ParseCustomFormat(input);
        }

        public List<CustomFormat> ParseCustomFormat(EpisodeFile episodeFile, Series series)
        {
            return ParseCustomFormat(episodeFile, series, _formatService.All());
        }

        public List<CustomFormat> ParseCustomFormat(EpisodeFile episodeFile)
        {
            return ParseCustomFormat(episodeFile, episodeFile.Series.Value, _formatService.All());
        }

        public List<CustomFormat> ParseCustomFormat(Blocklist blocklist, Series series)
        {
            var parsed = Parser.Parser.ParseTitle(blocklist.SourceTitle);

            var episodeInfo = new ParsedEpisodeInfo
            {
                SeriesTitle = series.Title,
                ReleaseTitle = parsed?.ReleaseTitle ?? blocklist.SourceTitle,
                Quality = blocklist.Quality,
                Languages = blocklist.Languages,
                ReleaseGroup = parsed?.ReleaseGroup
            };

            var input = new CustomFormatInput
            {
                EpisodeInfo = episodeInfo,
                Series = series,
                Size = blocklist.Size ?? 0,
                Languages = blocklist.Languages,
                IndexerFlags = blocklist.IndexerFlags,
                ReleaseType = blocklist.ReleaseType
            };

            return ParseCustomFormat(input);
        }

        public List<CustomFormat> ParseCustomFormat(EpisodeHistory history, Series series)
        {
            var parsed = Parser.Parser.ParseTitle(history.SourceTitle);

            long.TryParse(history.Data.GetValueOrDefault("size"), out var size);
            Enum.TryParse(history.Data.GetValueOrDefault("indexerFlags"), true, out IndexerFlags indexerFlags);
            Enum.TryParse(history.Data.GetValueOrDefault("releaseType"), out ReleaseType releaseType);

            var episodeInfo = new ParsedEpisodeInfo
            {
                SeriesTitle = series.Title,
                ReleaseTitle = parsed?.ReleaseTitle ?? history.SourceTitle,
                Quality = history.Quality,
                Languages = history.Languages,
                ReleaseGroup = parsed?.ReleaseGroup,
            };

            var input = new CustomFormatInput
            {
                EpisodeInfo = episodeInfo,
                Series = series,
                Size = size,
                Languages = history.Languages,
                IndexerFlags = indexerFlags,
                ReleaseType = releaseType
            };

            return ParseCustomFormat(input);
        }

        public List<CustomFormat> ParseCustomFormat(LocalEpisode localEpisode, string fileName)
        {
            var episodeInfo = new ParsedEpisodeInfo
            {
                SeriesTitle = localEpisode.Series.Title,
                ReleaseTitle = localEpisode.SceneName.IsNotNullOrWhiteSpace() ? localEpisode.SceneName : Path.GetFileName(localEpisode.Path),
                Quality = localEpisode.Quality,
                Languages = localEpisode.Languages,
                ReleaseGroup = localEpisode.ReleaseGroup
            };

            var input = new CustomFormatInput
            {
                EpisodeInfo = episodeInfo,
                Series = localEpisode.Series,
                Size = localEpisode.Size,
                Languages = localEpisode.Languages,
                IndexerFlags = localEpisode.IndexerFlags,
                ReleaseType = localEpisode.ReleaseType,
                Filename = fileName
            };

            return ParseCustomFormat(input);
        }

        private List<CustomFormat> ParseCustomFormat(CustomFormatInput input)
        {
            return ParseCustomFormat(input, _formatService.All());
        }

        private static List<CustomFormat> ParseCustomFormat(CustomFormatInput input, List<CustomFormat> allCustomFormats)
        {
            var matches = new List<CustomFormat>();

            foreach (var customFormat in allCustomFormats)
            {
                var specificationMatches = customFormat.Specifications
                    .GroupBy(t => t.GetType())
                    .Select(g => new SpecificationMatchesGroup
                    {
                        Matches = g.ToDictionary(t => t, t => t.IsSatisfiedBy(input))
                    })
                    .ToList();

                if (specificationMatches.All(x => x.DidMatch))
                {
                    matches.Add(customFormat);
                }
            }

            return matches.OrderBy(x => x.Name).ToList();
        }

        private List<CustomFormat> ParseCustomFormat(EpisodeFile episodeFile, Series series, List<CustomFormat> allCustomFormats)
        {
            var releaseTitle = string.Empty;

            if (episodeFile.SceneName.IsNotNullOrWhiteSpace())
            {
                _logger.Trace("Using scene name for release title: {0}", episodeFile.SceneName);
                releaseTitle = episodeFile.SceneName;
            }
            else if (episodeFile.OriginalFilePath.IsNotNullOrWhiteSpace())
            {
                _logger.Trace("Using original file path for release title: {0}", Path.GetFileName(episodeFile.OriginalFilePath));
                releaseTitle = Path.GetFileName(episodeFile.OriginalFilePath);
            }
            else if (episodeFile.RelativePath.IsNotNullOrWhiteSpace())
            {
                _logger.Trace("Using relative path for release title: {0}", Path.GetFileName(episodeFile.RelativePath));
                releaseTitle = Path.GetFileName(episodeFile.RelativePath);
            }

            var episodeInfo = new ParsedEpisodeInfo
            {
                SeriesTitle = series.Title,
                ReleaseTitle = releaseTitle,
                Quality = episodeFile.Quality,
                Languages = episodeFile.Languages,
                ReleaseGroup = episodeFile.ReleaseGroup,
            };

            var input = new CustomFormatInput
            {
                EpisodeInfo = episodeInfo,
                Series = series,
                Size = episodeFile.Size,
                Languages = episodeFile.Languages,
                IndexerFlags = episodeFile.IndexerFlags,
                ReleaseType = episodeFile.ReleaseType,
                Filename = Path.GetFileName(episodeFile.RelativePath),
            };

            return ParseCustomFormat(input, allCustomFormats);
        }
    }
}
