using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.ExternalImport
{
    public enum ExternalImportStatus
    {
        // Will be (or was) added.
        Add,

        // Already in Cinnabarr.
        Exists,

        // Can't be brought over; the reason says why.
        Skipped
    }

    public class ExternalImportItem
    {
        public string Name { get; set; }
        public ExternalImportStatus Status { get; set; }
        public string Reason { get; set; }
    }

    public class ExternalImportOptions
    {
        public ExternalInstallKind Kind { get; set; }
        public string Folder { get; set; }

        // For an install that sees its media at another path (a container): paths that start with PathFrom start with
        // PathTo here instead.
        public string PathFrom { get; set; }
        public string PathTo { get; set; }
    }

    public interface IExternalInstallImporter
    {
        // What importing would do, without doing it.
        List<ExternalImportItem> Preview(ExternalImportOptions options);

        // Does it, one item at a time: an item that can't be added is reported and the rest go on.
        List<ExternalImportItem> Import(ExternalImportOptions options);
    }

    /// <summary>
    /// Roadmap Phase 6 "importers": brings the library of an existing Sonarr or Radarr install, or the Torznab indexers of a
    /// Prowlarr install, into Cinnabarr. The files themselves are found again by the usual disk scan once an item is added.
    /// </summary>
    public class ExternalInstallImporter : IExternalInstallImporter
    {
        private readonly IExternalInstallReader _reader;
        private readonly ISeriesService _seriesService;
        private readonly IAddSeriesService _addSeriesService;
        private readonly IMovieService _movieService;
        private readonly IAddMovieService _addMovieService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly ITagService _tagService;
        private readonly IIndexerFactory _indexerFactory;
        private readonly IQBittorrentBackupReader _backupReader;
        private readonly IEngineControlService _engineControlService;
        private readonly Logger _logger;

        public ExternalInstallImporter(IExternalInstallReader reader,
                                       ISeriesService seriesService,
                                       IAddSeriesService addSeriesService,
                                       IMovieService movieService,
                                       IAddMovieService addMovieService,
                                       IQualityProfileService qualityProfileService,
                                       ITagService tagService,
                                       IIndexerFactory indexerFactory,
                                       IQBittorrentBackupReader backupReader,
                                       IEngineControlService engineControlService,
                                       Logger logger)
        {
            _reader = reader;
            _seriesService = seriesService;
            _addSeriesService = addSeriesService;
            _movieService = movieService;
            _addMovieService = addMovieService;
            _qualityProfileService = qualityProfileService;
            _tagService = tagService;
            _indexerFactory = indexerFactory;
            _backupReader = backupReader;
            _engineControlService = engineControlService;
            _logger = logger;
        }

        public List<ExternalImportItem> Preview(ExternalImportOptions options)
        {
            return options.Kind switch
            {
                ExternalInstallKind.Sonarr => PreviewSeries(options),
                ExternalInstallKind.Radarr => PreviewMovies(options),
                ExternalInstallKind.Prowlarr => PreviewIndexers(options),
                ExternalInstallKind.QBittorrent => PreviewTorrents(options),
                _ => throw new ArgumentOutOfRangeException(nameof(options))
            };
        }

        public List<ExternalImportItem> Import(ExternalImportOptions options)
        {
            var items = options.Kind switch
            {
                ExternalInstallKind.Sonarr => ImportSeries(options),
                ExternalInstallKind.Radarr => ImportMovies(options),
                ExternalInstallKind.Prowlarr => ImportIndexers(options),
                ExternalInstallKind.QBittorrent => ImportTorrents(options),
                _ => throw new ArgumentOutOfRangeException(nameof(options))
            };

            _logger.ProgressInfo("Import from {0} finished: {1} added, {2} already here, {3} skipped", options.Kind, items.Count(i => i.Status == ExternalImportStatus.Add), items.Count(i => i.Status == ExternalImportStatus.Exists), items.Count(i => i.Status == ExternalImportStatus.Skipped));

            return items;
        }

        // Series

        private List<ExternalImportItem> PreviewSeries(ExternalImportOptions options)
        {
            var existing = _seriesService.AllSeriesTvdbIds().Values.ToHashSet();

            return _reader.ReadSeries(options.Folder).Select(s => new ExternalImportItem
            {
                Name = s.Title,
                Status = existing.Contains(s.TvdbId) ? ExternalImportStatus.Exists : ExternalImportStatus.Add,
                Reason = ProfileNote(s.QualityProfileName)
            }).ToList();
        }

        private List<ExternalImportItem> ImportSeries(ExternalImportOptions options)
        {
            var existing = _seriesService.AllSeriesTvdbIds().Values.ToHashSet();
            var items = new List<ExternalImportItem>();

            foreach (var source in _reader.ReadSeries(options.Folder))
            {
                if (existing.Contains(source.TvdbId))
                {
                    items.Add(new ExternalImportItem { Name = source.Title, Status = ExternalImportStatus.Exists });
                    continue;
                }

                try
                {
                    _logger.ProgressInfo("Importing series '{0}'", source.Title);

                    var series = new Series
                    {
                        TvdbId = source.TvdbId,
                        Title = source.Title,
                        Path = Remap(source.Path, options),
                        Monitored = source.Monitored,
                        SeasonFolder = source.SeasonFolder,
                        SeriesType = (SeriesTypes)source.SeriesType,
                        QualityProfileId = FindProfile(source.QualityProfileName).Id,
                        Tags = GetTagIds(source.Tags),

                        // The episodes' own monitoring isn't read: a monitored series monitors all its episodes.
                        AddOptions = new AddSeriesOptions { Monitor = source.Monitored ? Tv.MonitorTypes.All : Tv.MonitorTypes.None }
                    };

                    _addSeriesService.AddSeries(series);
                    existing.Add(source.TvdbId);
                    items.Add(new ExternalImportItem { Name = source.Title, Status = ExternalImportStatus.Add, Reason = ProfileNote(source.QualityProfileName) });
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to import series '{0}'", source.Title);
                    items.Add(new ExternalImportItem { Name = source.Title, Status = ExternalImportStatus.Skipped, Reason = ex.Message });
                }
            }

            return items;
        }

        // Movies

        private List<ExternalImportItem> PreviewMovies(ExternalImportOptions options)
        {
            var existing = _movieService.AllMovieTmdbIds().ToHashSet();

            return _reader.ReadMovies(options.Folder).Select(m => new ExternalImportItem
            {
                Name = m.Year > 0 ? $"{m.Title} ({m.Year})" : m.Title,
                Status = existing.Contains(m.TmdbId) ? ExternalImportStatus.Exists : ExternalImportStatus.Add,
                Reason = ProfileNote(m.QualityProfileName)
            }).ToList();
        }

        private List<ExternalImportItem> ImportMovies(ExternalImportOptions options)
        {
            var existing = _movieService.AllMovieTmdbIds().ToHashSet();
            var items = new List<ExternalImportItem>();

            foreach (var source in _reader.ReadMovies(options.Folder))
            {
                var name = source.Year > 0 ? $"{source.Title} ({source.Year})" : source.Title;

                if (existing.Contains(source.TmdbId))
                {
                    items.Add(new ExternalImportItem { Name = name, Status = ExternalImportStatus.Exists });
                    continue;
                }

                try
                {
                    _logger.ProgressInfo("Importing movie '{0}'", name);

                    var movie = new Movie
                    {
                        TmdbId = source.TmdbId,
                        Title = source.Title,
                        Path = Remap(source.Path, options),
                        Monitored = source.Monitored,
                        MinimumAvailability = (MovieStatusType)source.MinimumAvailability,
                        QualityProfileId = FindProfile(source.QualityProfileName).Id,
                        Tags = GetTagIds(source.Tags),
                        AddOptions = new AddMovieOptions { AddMethod = AddMovieMethod.List }
                    };

                    _addMovieService.AddMovie(movie);
                    existing.Add(source.TmdbId);
                    items.Add(new ExternalImportItem { Name = name, Status = ExternalImportStatus.Add, Reason = ProfileNote(source.QualityProfileName) });
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to import movie '{0}'", name);
                    items.Add(new ExternalImportItem { Name = name, Status = ExternalImportStatus.Skipped, Reason = ex.Message });
                }
            }

            return items;
        }

        // Torrents of another qBittorrent

        private List<ExternalImportItem> PreviewTorrents(ExternalImportOptions options)
        {
            var inEngine = _engineControlService.GetTorrentIds();

            return _backupReader.Read(options.Folder).Select(t => DescribeTorrent(t, options, inEngine)).ToList();
        }

        private List<ExternalImportItem> ImportTorrents(ExternalImportOptions options)
        {
            var inEngine = _engineControlService.GetTorrentIds();
            var items = new List<ExternalImportItem>();

            foreach (var torrent in _backupReader.Read(options.Folder))
            {
                var item = DescribeTorrent(torrent, options, inEngine);

                if (item.Status == ExternalImportStatus.Add)
                {
                    try
                    {
                        _logger.ProgressInfo("Taking over torrent '{0}'", torrent.Name);

                        _engineControlService.AdoptTorrentFile(Path.GetFileName(torrent.TorrentFile), File.ReadAllBytes(torrent.TorrentFile), Remap(torrent.SavePath, options), torrent.Stopped);
                        inEngine.Add(torrent.Hash);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Unable to take over torrent '{0}'", torrent.Name);
                        item.Status = ExternalImportStatus.Skipped;
                        item.Reason = ex.Message;
                    }
                }

                items.Add(item);
            }

            return items;
        }

        // A torrent is taken over when the engine doesn't have it and its files' folder can be reached from here.
        private static ExternalImportItem DescribeTorrent(ExternalTorrent torrent, ExternalImportOptions options, HashSet<string> inEngine)
        {
            var savePath = Remap(torrent.SavePath, options);

            if (inEngine.Contains(torrent.Hash))
            {
                return new ExternalImportItem { Name = torrent.Name, Status = ExternalImportStatus.Exists };
            }

            if (!Directory.Exists(savePath))
            {
                return new ExternalImportItem { Name = torrent.Name, Status = ExternalImportStatus.Skipped, Reason = $"The folder '{savePath}' can't be reached from here; mount it, or use the path boxes to say where it is" };
            }

            return new ExternalImportItem { Name = torrent.Name, Status = ExternalImportStatus.Add };
        }

        // Indexers

        private List<ExternalImportItem> PreviewIndexers(ExternalImportOptions options)
        {
            var existingUrls = ExistingIndexerUrls();

            return _reader.ReadIndexers(options.Folder).Select(i => Describe(i, existingUrls)).ToList();
        }

        private List<ExternalImportItem> ImportIndexers(ExternalImportOptions options)
        {
            var existingUrls = ExistingIndexerUrls();
            var items = new List<ExternalImportItem>();

            foreach (var source in _reader.ReadIndexers(options.Folder))
            {
                var item = Describe(source, existingUrls);

                if (item.Status == ExternalImportStatus.Add)
                {
                    try
                    {
                        var settings = Json.Deserialize<TorznabSettings>(source.SettingsJson);

                        // The categories a Torznab indexer needs to be valid: TV and movies.
                        settings.Categories = settings.Categories.Empty() ? new[] { 5030, 5040 } : settings.Categories;

                        _indexerFactory.Create(new IndexerDefinition
                        {
                            Name = source.Name,
                            Implementation = "Torznab",
                            Settings = settings,
                            Priority = source.Priority,
                            EnableRss = source.EnableRss,
                            EnableAutomaticSearch = source.EnableAutomaticSearch,
                            EnableInteractiveSearch = source.EnableInteractiveSearch
                        });

                        existingUrls.Add(settings.BaseUrl);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Unable to import indexer '{0}'", source.Name);
                        item.Status = ExternalImportStatus.Skipped;
                        item.Reason = ex.Message;
                    }
                }

                items.Add(item);
            }

            return items;
        }

        private ExternalImportItem Describe(ExternalIndexer source, HashSet<string> existingUrls)
        {
            if (!string.Equals(source.Implementation, "Torznab", StringComparison.OrdinalIgnoreCase))
            {
                return new ExternalImportItem { Name = source.Name, Status = ExternalImportStatus.Skipped, Reason = $"A {source.Implementation} indexer can't be brought over yet; only Torznab indexers can" };
            }

            var baseUrl = Json.Deserialize<TorznabSettings>(source.SettingsJson)?.BaseUrl;

            return new ExternalImportItem
            {
                Name = source.Name,
                Status = baseUrl.IsNotNullOrWhiteSpace() && existingUrls.Contains(baseUrl) ? ExternalImportStatus.Exists : ExternalImportStatus.Add
            };
        }

        private HashSet<string> ExistingIndexerUrls()
        {
            return _indexerFactory.All()
                .Select(i => (i.Settings as TorznabSettings)?.BaseUrl)
                .Where(u => u.IsNotNullOrWhiteSpace())
                .ToHashSet();
        }

        // Shared

        // The profile of that name here, or the first one (the note says so).
        private QualityProfile FindProfile(string name)
        {
            var profiles = _qualityProfileService.All();

            return profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? profiles.First();
        }

        private string ProfileNote(string profileName)
        {
            var found = _qualityProfileService.All().Any(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));

            return found ? null : $"There is no quality profile named '{profileName}' here; the first profile is used";
        }

        private HashSet<int> GetTagIds(IEnumerable<string> labels)
        {
            var ids = new HashSet<int>();

            foreach (var label in labels)
            {
                var tag = _tagService.All().FirstOrDefault(t => string.Equals(t.Label, label, StringComparison.OrdinalIgnoreCase)) ?? _tagService.Add(new Tag { Label = label });

                ids.Add(tag.Id);
            }

            return ids;
        }

        private static string Remap(string path, ExternalImportOptions options)
        {
            if (path.IsNullOrWhiteSpace() || options.PathFrom.IsNullOrWhiteSpace() || options.PathTo.IsNullOrWhiteSpace())
            {
                return path;
            }

            return path.StartsWith(options.PathFrom, StringComparison.Ordinal) ? string.Concat(options.PathTo, path.AsSpan(options.PathFrom.Length)) : path;
        }
    }
}
