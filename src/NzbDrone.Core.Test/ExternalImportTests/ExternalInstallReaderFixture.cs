using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ExternalImport;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ExternalImportTests
{
    [TestFixture]
    public class ExternalInstallReaderFixture : CoreTest<ExternalInstallReader>
    {
        private string _folder;

        [SetUp]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), "cinnabarr-reader-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            Directory.Delete(_folder, true);
        }

        private void GivenDatabase(string fileName, params string[] statements)
        {
            using var connection = new SQLiteConnection($"Data Source={Path.Combine(_folder, fileName)};Pooling=False");
            connection.Open();

            foreach (var statement in statements)
            {
                using var command = new SQLiteCommand(statement, connection);
                command.ExecuteNonQuery();
            }
        }

        [Test]
        public void should_read_series_with_their_profile_name_and_tag_labels()
        {
            GivenDatabase(
                "sonarr.db",
                "CREATE TABLE QualityProfiles (Id INTEGER PRIMARY KEY, Name TEXT)",
                "CREATE TABLE Tags (Id INTEGER PRIMARY KEY, Label TEXT)",
                "CREATE TABLE Series (Id INTEGER PRIMARY KEY, TvdbId INTEGER, Title TEXT, Path TEXT, Monitored INTEGER, SeriesType INTEGER, SeasonFolder INTEGER, QualityProfileId INTEGER, Tags TEXT)",
                "INSERT INTO QualityProfiles VALUES (4, 'HD-1080p')",
                "INSERT INTO Tags VALUES (1, 'kids'), (2, 'anime')",
                "INSERT INTO Series VALUES (1, 81189, 'Breaking Bad', '/tv/Breaking Bad', 1, 0, 1, 4, '[1,2,9]')");

            var series = Subject.ReadSeries(_folder).Single();

            series.TvdbId.Should().Be(81189);
            series.Title.Should().Be("Breaking Bad");
            series.Path.Should().Be("/tv/Breaking Bad");
            series.Monitored.Should().BeTrue();
            series.SeasonFolder.Should().BeTrue();
            series.QualityProfileName.Should().Be("HD-1080p");
            series.Tags.Should().Equal("kids", "anime");
        }

        [Test]
        public void should_read_movies_joined_with_their_metadata()
        {
            GivenDatabase(
                "radarr.db",
                "CREATE TABLE QualityProfiles (Id INTEGER PRIMARY KEY, Name TEXT)",
                "CREATE TABLE Tags (Id INTEGER PRIMARY KEY, Label TEXT)",
                "CREATE TABLE MovieMetadata (Id INTEGER PRIMARY KEY, TmdbId INTEGER, Title TEXT, Year INTEGER)",
                "CREATE TABLE Movies (Id INTEGER PRIMARY KEY, MovieMetadataId INTEGER, Path TEXT, Monitored INTEGER, MinimumAvailability INTEGER, QualityProfileId INTEGER, Tags TEXT)",
                "INSERT INTO QualityProfiles VALUES (2, 'Any')",
                "INSERT INTO MovieMetadata VALUES (7, 603, 'The Matrix', 1999)",
                "INSERT INTO Movies VALUES (1, 7, '/movies/The Matrix (1999)', 1, 3, 2, '[]')");

            var movie = Subject.ReadMovies(_folder).Single();

            movie.TmdbId.Should().Be(603);
            movie.Title.Should().Be("The Matrix");
            movie.Year.Should().Be(1999);
            movie.MinimumAvailability.Should().Be(3);
            movie.QualityProfileName.Should().Be("Any");
            movie.Tags.Should().BeEmpty();
        }

        [Test]
        public void should_read_indexers_with_their_settings()
        {
            GivenDatabase(
                "prowlarr.db",
                "CREATE TABLE Indexers (Id INTEGER PRIMARY KEY, Name TEXT, Implementation TEXT, Settings TEXT, Priority INTEGER, EnableRss INTEGER, EnableAutomaticSearch INTEGER, EnableInteractiveSearch INTEGER)",
                "INSERT INTO Indexers VALUES (1, 'My Torznab', 'Torznab', '{\"baseUrl\":\"http://x\",\"apiKey\":\"k\"}', 25, 1, 1, 0)");

            var indexer = Subject.ReadIndexers(_folder).Single();

            indexer.Name.Should().Be("My Torznab");
            indexer.Implementation.Should().Be("Torznab");
            indexer.SettingsJson.Should().Contain("http://x");
            indexer.EnableRss.Should().BeTrue();
            indexer.EnableInteractiveSearch.Should().BeFalse();
        }

        [Test]
        public void should_say_so_when_the_folder_has_no_database_and_leave_the_original_alone()
        {
            Assert.Throws<FileNotFoundException>(() => Subject.ReadSeries(_folder));

            GivenDatabase("sonarr.db", "CREATE TABLE QualityProfiles (Id INTEGER PRIMARY KEY, Name TEXT)", "CREATE TABLE Tags (Id INTEGER PRIMARY KEY, Label TEXT)", "CREATE TABLE Series (Id INTEGER PRIMARY KEY, TvdbId INTEGER, Title TEXT, Path TEXT, Monitored INTEGER, SeriesType INTEGER, SeasonFolder INTEGER, QualityProfileId INTEGER, Tags TEXT)");
            var before = File.GetLastWriteTimeUtc(Path.Combine(_folder, "sonarr.db"));

            Subject.ReadSeries(_folder).Should().BeEmpty();

            File.GetLastWriteTimeUtc(Path.Combine(_folder, "sonarr.db")).Should().Be(before);
            Directory.GetFiles(Path.GetTempPath(), "cinnabarr-import-*").Should().BeEmpty();
        }
    }
}
