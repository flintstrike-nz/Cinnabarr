using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.ExternalImport
{
    public enum ExternalInstallKind
    {
        Sonarr,
        Radarr,
        Prowlarr
    }

    public class ExternalSeries
    {
        public int TvdbId { get; set; }
        public string Title { get; set; }
        public string Path { get; set; }
        public bool Monitored { get; set; }
        public int SeriesType { get; set; }
        public bool SeasonFolder { get; set; }
        public string QualityProfileName { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    public class ExternalMovie
    {
        public int TmdbId { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public string Path { get; set; }
        public bool Monitored { get; set; }
        public int MinimumAvailability { get; set; }
        public string QualityProfileName { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    public class ExternalIndexer
    {
        public string Name { get; set; }
        public string Implementation { get; set; }
        public string SettingsJson { get; set; }
        public int Priority { get; set; }
        public bool EnableRss { get; set; }
        public bool EnableAutomaticSearch { get; set; }
        public bool EnableInteractiveSearch { get; set; }
    }

    public interface IExternalInstallReader
    {
        List<ExternalSeries> ReadSeries(string folder);
        List<ExternalMovie> ReadMovies(string folder);
        List<ExternalIndexer> ReadIndexers(string folder);
    }

    /// <summary>Reads another install's database (a Sonarr, Radarr or Prowlarr data folder) without touching it: a copy is opened.</summary>
    public class ExternalInstallReader : IExternalInstallReader
    {
        public List<ExternalSeries> ReadSeries(string folder)
        {
            return Read(folder, "sonarr.db", connection =>
            {
                var profiles = ReadNames(connection, "QualityProfiles", "Name");
                var tags = ReadNames(connection, "Tags", "Label");

                return Query(connection, "SELECT TvdbId, Title, Path, Monitored, SeriesType, SeasonFolder, QualityProfileId, Tags FROM Series", r => new ExternalSeries
                {
                    TvdbId = r.GetInt32(0),
                    Title = r.GetString(1),
                    Path = r.IsDBNull(2) ? null : r.GetString(2),
                    Monitored = r.GetBoolean(3),
                    SeriesType = r.GetInt32(4),
                    SeasonFolder = r.GetBoolean(5),
                    QualityProfileName = profiles.GetValueOrDefault(r.GetInt32(6)),
                    Tags = ReadTagLabels(r.IsDBNull(7) ? null : r.GetString(7), tags)
                });
            });
        }

        public List<ExternalMovie> ReadMovies(string folder)
        {
            return Read(folder, "radarr.db", connection =>
            {
                var profiles = ReadNames(connection, "QualityProfiles", "Name");
                var tags = ReadNames(connection, "Tags", "Label");

                return Query(connection, "SELECT m.TmdbId, m.Title, m.Year, v.Path, v.Monitored, v.MinimumAvailability, v.QualityProfileId, v.Tags FROM Movies v JOIN MovieMetadata m ON m.Id = v.MovieMetadataId", r => new ExternalMovie
                {
                    TmdbId = r.GetInt32(0),
                    Title = r.GetString(1),
                    Year = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                    Path = r.IsDBNull(3) ? null : r.GetString(3),
                    Monitored = r.GetBoolean(4),
                    MinimumAvailability = r.GetInt32(5),
                    QualityProfileName = profiles.GetValueOrDefault(r.GetInt32(6)),
                    Tags = ReadTagLabels(r.IsDBNull(7) ? null : r.GetString(7), tags)
                });
            });
        }

        public List<ExternalIndexer> ReadIndexers(string folder)
        {
            return Read(folder, "prowlarr.db", connection =>
                Query(connection, "SELECT Name, Implementation, Settings, Priority, EnableRss, EnableAutomaticSearch, EnableInteractiveSearch FROM Indexers", r => new ExternalIndexer
                {
                    Name = r.GetString(0),
                    Implementation = r.GetString(1),
                    SettingsJson = r.IsDBNull(2) ? "{}" : r.GetString(2),
                    Priority = r.GetInt32(3),
                    EnableRss = r.GetBoolean(4),
                    EnableAutomaticSearch = r.GetBoolean(5),
                    EnableInteractiveSearch = r.GetBoolean(6)
                }));
        }

        private static List<T> Read<T>(string folder, string fileName, Func<SQLiteConnection, List<T>> read)
        {
            var source = Path.Combine(folder ?? string.Empty, fileName);

            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"There is no {fileName} in that folder");
            }

            // A copy, so the other install can keep running and is never changed (its write-ahead files come along).
            var temp = Path.Combine(Path.GetTempPath(), "cinnabarr-import-" + Guid.NewGuid().ToString("N") + ".db");

            try
            {
                File.Copy(source, temp);

                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    if (File.Exists(source + suffix))
                    {
                        File.Copy(source + suffix, temp + suffix);
                    }
                }

                using var connection = new SQLiteConnection($"Data Source={temp};Read Only=True;Pooling=False");
                connection.Open();

                return read(connection);
            }
            finally
            {
                SQLiteConnection.ClearAllPools();

                foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                {
                    try
                    {
                        File.Delete(temp + suffix);
                    }
                    catch (IOException)
                    {
                        // A leftover temp file is harmless.
                    }
                }
            }
        }

        private static List<T> Query<T>(SQLiteConnection connection, string sql, Func<IDataRecord, T> map)
        {
            using var command = new SQLiteCommand(sql, connection);
            using var reader = command.ExecuteReader();
            var items = new List<T>();

            while (reader.Read())
            {
                items.Add(map(reader));
            }

            return items;
        }

        private static Dictionary<int, string> ReadNames(SQLiteConnection connection, string table, string column)
        {
            return Query(connection, $"SELECT Id, {column} FROM {table}", r => (Id: r.GetInt32(0), Name: r.GetString(1))).ToDictionary(x => x.Id, x => x.Name);
        }

        private static List<string> ReadTagLabels(string json, Dictionary<int, string> tags)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<string>();
            }

            return Json.Deserialize<List<int>>(json).Where(tags.ContainsKey).Select(id => tags[id]).ToList();
        }
    }
}
