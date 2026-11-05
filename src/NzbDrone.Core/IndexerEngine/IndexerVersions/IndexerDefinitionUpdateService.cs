using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerEngine.Indexers.Definitions.Cardigann;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NzbDrone.Core.IndexerEngine.IndexerVersions
{
    public interface IIndexerDefinitionUpdateService
    {
        List<CardigannMetaDefinition> All();
        CardigannDefinition GetCachedDefinition(string fileKey);
        List<string> GetBlocklist();
    }

    public class IndexerDefinitionUpdateService : IIndexerDefinitionUpdateService, IExecute<IndexerDefinitionUpdateCommand>, IHandle<ApplicationStartedEvent>
    {
        /* Update Service will fall back if version # does not exist for an indexer  per Ta */

        private const int DEFINITION_VERSION = 12;

        // Used when moving yml to C#
        private readonly List<string> _definitionBlocklist = new()
        {
            "aither",
            "animeworld",
            "audiobookbay",
            "beyond-hd-oneurl",
            "beyond-hd",
            "blutopia",
            "brsociety",
            "danishbytes",
            "datascene",
            "desitorrents",
            "hdbits",
            "lat-team",
            "mteamtp",
            "mteamtp2fa",
            "reelflix",
            "shareisland",
            "skipthecommercials",
            "tellytorrent"
        };

        // definitions/v{n}/{name}.yml inside the downloaded repository zip (the folder above it is the repository's own).
        private static readonly Regex ZipEntryRegex = new(@"(?:^|/)definitions/v(?<version>\d+)/(?<file>[A-Za-z0-9._-]+\.yml)$", RegexOptions.Compiled);

        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly ICached<CardigannDefinition> _cache;
        private readonly Logger _logger;

        private readonly IDeserializer _deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        public IndexerDefinitionUpdateService(IHttpClient httpClient,
                                          IConfigService configService,
                                          IAppFolderInfo appFolderInfo,
                                          IDiskProvider diskProvider,
                                          ICacheManager cacheManager,
                                          Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _httpClient = httpClient;
            _configService = configService;
            _logger = logger;

            _cache = cacheManager.GetCache<CardigannDefinition>(typeof(CardigannDefinition), "definitions");
        }

        public List<CardigannMetaDefinition> All()
        {
            var indexerList = new List<CardigannMetaDefinition>();

            try
            {
                // EMBARR: definitions are read from disk (filled by UpdateLocalDefinitions), never from Prowlarr's servers.
                var definitionFolder = Path.Combine(_appFolderInfo.AppDataFolder, "Definitions");

                indexerList = ReadDefinitionsFromDisk(indexerList, definitionFolder)
                    .Where(i => !_definitionBlocklist.Contains(i.File))
                    .ToList();

                //Check for custom definitions
                var customDefinitionFolder = Path.Combine(_appFolderInfo.AppDataFolder, "Definitions", "Custom");

                indexerList = ReadDefinitionsFromDisk(indexerList, customDefinitionFolder);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to Connect to Indexer Definition Server for Indexer listing");
            }

            return indexerList;
        }

        public CardigannDefinition GetCachedDefinition(string fileKey)
        {
            if (string.IsNullOrWhiteSpace(fileKey))
            {
                throw new ArgumentNullException(nameof(fileKey));
            }

            var definition = _cache.Get(fileKey, () => GetUncachedDefinition(fileKey));

            return definition;
        }

        public List<string> GetBlocklist()
        {
            return _definitionBlocklist;
        }

        private List<CardigannMetaDefinition> ReadDefinitionsFromDisk(List<CardigannMetaDefinition> defs, string path, SearchOption options = SearchOption.TopDirectoryOnly)
        {
            var indexerList = defs;

            var directoryInfo = new DirectoryInfo(path);

            if (directoryInfo.Exists)
            {
                var files = directoryInfo.GetFiles("*.yml", options);

                foreach (var file in files)
                {
                    _logger.Debug("Loading definition {0}", file.FullName);

                    try
                    {
                        var definitionString = File.ReadAllText(file.FullName);
                        var definition = _deserializer.Deserialize<CardigannMetaDefinition>(definitionString);

                        definition.File = Path.GetFileNameWithoutExtension(file.Name);

                        if (indexerList.Any(i => i.File == definition.File || i.Name == definition.Name))
                        {
                            _logger.Warn("Definition {0} does not have unique file name or Indexer name", file.FullName);
                            continue;
                        }

                        indexerList.Add(definition);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error while parsing Cardigann definition {0}", file.FullName);
                    }
                }
            }

            return indexerList;
        }

        private CardigannDefinition GetUncachedDefinition(string fileKey)
        {
            if (string.IsNullOrWhiteSpace(fileKey))
            {
                throw new ArgumentNullException(nameof(fileKey));
            }

            EnsureDefinitionsFolder();

            var definitionFolder = Path.Combine(_appFolderInfo.AppDataFolder, "Definitions");

            var directoryInfo = new DirectoryInfo(definitionFolder);

            if (directoryInfo.Exists)
            {
                var files = directoryInfo.GetFiles($"{fileKey}.yml", SearchOption.AllDirectories);

                if (files.Any())
                {
                    var file = files.First();
                    _logger.Trace("Loading Cardigann definition {0}", file.FullName);

                    try
                    {
                        var definitionString = File.ReadAllText(file.FullName);
                        var definition = _deserializer.Deserialize<CardigannDefinition>(definitionString);

                        return CleanIndexerDefinition(definition);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error while parsing Cardigann definition {0}", file.FullName);
                    }
                }
            }

            var dbDefs = All();

            //Check to ensure it's in versioned defs before we go to web
            if (dbDefs.Count > 0 && dbDefs.All(x => x.File != fileKey))
            {
                throw new ArgumentNullException(nameof(fileKey));
            }

            // EMBARR: no web lookup; a definition that isn't on disk doesn't exist (update the definitions to fetch new ones).
            throw new Exception($"Indexer definition for '{fileKey}' does not exist.");
        }

        private CardigannDefinition CleanIndexerDefinition(CardigannDefinition definition)
        {
            if (definition.Settings == null)
            {
                definition.Settings = new List<SettingsField>
                {
                    new() { Name = "username", Label = "Username", Type = "text" },
                    new() { Name = "password", Label = "Password", Type = "password" }
                };
            }

            if (definition.Encoding == null)
            {
                definition.Encoding = "UTF-8";
            }

            if (definition.Login != null && definition.Login.Method == null)
            {
                definition.Login.Method = "form";
            }

            if (definition.Search.Paths == null)
            {
                definition.Search.Paths = new List<SearchPathBlock>();
            }

            // convert definitions with a single search Path to a Paths entry
            if (definition.Search.Path != null)
            {
                definition.Search.Paths.Add(new SearchPathBlock
                {
                    Path = definition.Search.Path,
                    Inheritinputs = true
                });
            }

            return definition;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            // EMBARR: switched off until the engine is in use (roadmap Phase 3, step 3b), so Embarr doesn't download
            // definitions at every start for nothing.
        }

        public void Execute(IndexerDefinitionUpdateCommand message)
        {
            UpdateLocalDefinitions();
        }

        private void EnsureDefinitionsFolder()
        {
            var definitionFolder = Path.Combine(_appFolderInfo.AppDataFolder, "Definitions");

            _diskProvider.CreateFolder(definitionFolder);
        }

        // Downloads the definitions zip from the configured source and keeps, for each indexer, its definition from the
        // highest version up to DEFINITION_VERSION (what Prowlarr's server does when it builds its package). With no
        // definitions on disk and no download, a snapshot shipped next to the app is used.
        private void UpdateLocalDefinitions()
        {
            var definitionsFolder = Path.Combine(_appFolderInfo.AppDataFolder, "Definitions");

            try
            {
                EnsureDefinitionsFolder();

                var saveFile = Path.Combine(definitionsFolder, "indexers.zip");

                try
                {
                    _httpClient.DownloadFile(_configService.IndexerDefinitionsUrl, saveFile);

                    var count = ExtractDefinitions(saveFile, definitionsFolder);

                    _cache.Clear();

                    _logger.Debug("Updated {0} indexer definitions", count);
                }
                finally
                {
                    if (_diskProvider.FileExists(saveFile))
                    {
                        _diskProvider.DeleteFile(saveFile);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Definition update failed");

                CopyBundledDefinitions(definitionsFolder);
            }
        }

        private int ExtractDefinitions(string zipPath, string definitionsFolder)
        {
            using var archive = ZipFile.OpenRead(zipPath);

            var best = new Dictionary<string, (int Version, ZipArchiveEntry Entry)>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in archive.Entries)
            {
                var match = ZipEntryRegex.Match(entry.FullName);

                if (!match.Success || !int.TryParse(match.Groups["version"].Value, out var version) || version > DEFINITION_VERSION)
                {
                    continue;
                }

                var file = match.Groups["file"].Value;

                if (!best.TryGetValue(file, out var current) || version > current.Version)
                {
                    best[file] = (version, entry);
                }
            }

            if (best.Count == 0)
            {
                throw new InvalidDataException("The definitions download has no definitions/v* folders.");
            }

            var written = 0;

            foreach (var (file, (_, entry)) in best)
            {
                // Only the validated file name is used, never the entry's own path.
                if (_definitionBlocklist.Contains(Path.GetFileNameWithoutExtension(file)))
                {
                    continue;
                }

                entry.ExtractToFile(Path.Combine(definitionsFolder, file), true);
                written++;
            }

            return written;
        }

        private void CopyBundledDefinitions(string definitionsFolder)
        {
            try
            {
                var bundled = Path.Combine(_appFolderInfo.StartUpFolder, "Definitions");

                if (!Directory.Exists(bundled) || new DirectoryInfo(definitionsFolder).GetFiles("*.yml").Any())
                {
                    return;
                }

                foreach (var file in new DirectoryInfo(bundled).GetFiles("*.yml"))
                {
                    file.CopyTo(Path.Combine(definitionsFolder, file.Name), true);
                }

                _logger.Info("Using the indexer definitions shipped with the app");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to use the bundled indexer definitions");
            }
        }
    }
}
