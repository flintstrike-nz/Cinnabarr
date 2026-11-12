using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.ExternalImport;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ExternalImportTests
{
    [TestFixture]
    public class ExternalInstallImporterFixture : CoreTest<ExternalInstallImporter>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IQualityProfileService>().Setup(q => q.All()).Returns(new List<QualityProfile>
            {
                new QualityProfile { Id = 1, Name = "Any" },
                new QualityProfile { Id = 4, Name = "HD-1080p" }
            });

            Mocker.GetMock<ITagService>().Setup(t => t.All()).Returns(new List<Tag> { new Tag { Id = 3, Label = "kids" } });
            Mocker.GetMock<ITagService>().Setup(t => t.Add(It.IsAny<Tag>())).Returns((Tag t) => new Tag { Id = 8, Label = t.Label });
            Mocker.GetMock<ISeriesService>().Setup(s => s.AllSeriesTvdbIds()).Returns(new Dictionary<int, int> { { 1, 100 } });
            Mocker.GetMock<IMovieService>().Setup(s => s.AllMovieTmdbIds()).Returns(new List<int> { 603 });
            Mocker.GetMock<IIndexerFactory>().Setup(f => f.All()).Returns(new List<IndexerDefinition>());
        }

        private static ExternalImportOptions Options(ExternalInstallKind kind) => new ExternalImportOptions { Kind = kind, Folder = "/other" };

        [Test]
        public void should_preview_series_as_new_or_already_here_and_note_a_missing_profile()
        {
            Mocker.GetMock<IExternalInstallReader>().Setup(r => r.ReadSeries("/other")).Returns(new List<ExternalSeries>
            {
                new ExternalSeries { TvdbId = 100, Title = "Here", QualityProfileName = "Any" },
                new ExternalSeries { TvdbId = 200, Title = "New", QualityProfileName = "Ultra" }
            });

            var items = Subject.Preview(Options(ExternalInstallKind.Sonarr));

            items[0].Status.Should().Be(ExternalImportStatus.Exists);
            items[1].Status.Should().Be(ExternalImportStatus.Add);
            items[1].Reason.Should().Contain("Ultra");
        }

        [Test]
        public void should_add_a_series_with_its_profile_tags_monitoring_and_a_remapped_path()
        {
            Mocker.GetMock<IExternalInstallReader>().Setup(r => r.ReadSeries("/other")).Returns(new List<ExternalSeries>
            {
                new ExternalSeries { TvdbId = 200, Title = "New", Path = "/tv/New", Monitored = true, SeriesType = 2, SeasonFolder = true, QualityProfileName = "hd-1080p", Tags = new List<string> { "kids", "fresh" } }
            });

            Series added = null;
            Mocker.GetMock<IAddSeriesService>().Setup(a => a.AddSeries(It.IsAny<Series>())).Callback<Series>(s => added = s).Returns((Series s) => s);

            var options = Options(ExternalInstallKind.Sonarr);
            options.PathFrom = "/tv";
            options.PathTo = "/media/tv";

            var items = Subject.Import(options);

            items.Single().Status.Should().Be(ExternalImportStatus.Add);
            added.TvdbId.Should().Be(200);
            added.Path.Should().Be("/media/tv/New");
            added.QualityProfileId.Should().Be(4);
            added.SeriesType.Should().Be(SeriesTypes.Anime);
            added.Tags.Should().BeEquivalentTo(new[] { 3, 8 });
            added.AddOptions.Monitor.Should().Be(NzbDrone.Core.Tv.MonitorTypes.All);
        }

        [Test]
        public void should_report_an_item_that_cannot_be_added_and_go_on_with_the_rest()
        {
            Mocker.GetMock<IExternalInstallReader>().Setup(r => r.ReadSeries("/other")).Returns(new List<ExternalSeries>
            {
                new ExternalSeries { TvdbId = 201, Title = "Bad", QualityProfileName = "Any" },
                new ExternalSeries { TvdbId = 202, Title = "Good", QualityProfileName = "Any" }
            });

            Mocker.GetMock<IAddSeriesService>().Setup(a => a.AddSeries(It.Is<Series>(s => s.TvdbId == 201))).Throws(new InvalidOperationException("path is missing"));

            var items = Subject.Import(Options(ExternalInstallKind.Sonarr));

            items[0].Status.Should().Be(ExternalImportStatus.Skipped);
            items[0].Reason.Should().Contain("path is missing");
            items[1].Status.Should().Be(ExternalImportStatus.Add);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_add_a_movie_and_skip_one_already_here()
        {
            Mocker.GetMock<IExternalInstallReader>().Setup(r => r.ReadMovies("/other")).Returns(new List<ExternalMovie>
            {
                new ExternalMovie { TmdbId = 603, Title = "The Matrix", Year = 1999, QualityProfileName = "Any" },
                new ExternalMovie { TmdbId = 604, Title = "Reloaded", Year = 2003, Path = "/movies/Reloaded (2003)", Monitored = true, MinimumAvailability = 3, QualityProfileName = "HD-1080p" }
            });

            Movie added = null;
            Mocker.GetMock<IAddMovieService>().Setup(a => a.AddMovie(It.IsAny<Movie>())).Callback<Movie>(m => added = m).Returns((Movie m) => m);

            var items = Subject.Import(Options(ExternalInstallKind.Radarr));

            items[0].Status.Should().Be(ExternalImportStatus.Exists);
            items[1].Status.Should().Be(ExternalImportStatus.Add);
            added.TmdbId.Should().Be(604);
            added.QualityProfileId.Should().Be(4);
            added.MinimumAvailability.Should().Be(MovieStatusType.Released);
        }

        [Test]
        public void should_import_torznab_indexers_and_skip_other_kinds_and_ones_already_here()
        {
            Mocker.GetMock<IExternalInstallReader>().Setup(r => r.ReadIndexers("/other")).Returns(new List<ExternalIndexer>
            {
                new ExternalIndexer { Name = "New", Implementation = "Torznab", SettingsJson = "{\"baseUrl\":\"http://new\",\"apiKey\":\"k\"}", Priority = 10, EnableRss = true },
                new ExternalIndexer { Name = "Same", Implementation = "Torznab", SettingsJson = "{\"baseUrl\":\"http://same\"}" },
                new ExternalIndexer { Name = "Tracker", Implementation = "Cardigann", SettingsJson = "{}" }
            });

            Mocker.GetMock<IIndexerFactory>().Setup(f => f.All()).Returns(new List<IndexerDefinition> { new IndexerDefinition { Settings = new TorznabSettings { BaseUrl = "http://same" } } });

            IndexerDefinition created = null;
            Mocker.GetMock<IIndexerFactory>().Setup(f => f.Create(It.IsAny<IndexerDefinition>())).Callback<IndexerDefinition>(d => created = d).Returns((IndexerDefinition d) => d);

            var items = Subject.Import(Options(ExternalInstallKind.Prowlarr));

            items.Select(i => i.Status).Should().Equal(ExternalImportStatus.Add, ExternalImportStatus.Exists, ExternalImportStatus.Skipped);
            created.Name.Should().Be("New");
            created.Priority.Should().Be(10);
            ((TorznabSettings)created.Settings).BaseUrl.Should().Be("http://new");
            ((TorznabSettings)created.Settings).Categories.Should().NotBeEmpty();
        }

        [Test]
        public void should_take_over_torrents_the_engine_does_not_have_and_whose_folder_can_be_reached()
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cinnabarr-adopt-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(folder);
            var torrentFile = System.IO.Path.Combine(folder, "a.torrent");
            System.IO.File.WriteAllBytes(torrentFile, new byte[] { 1, 2, 3 });

            try
            {
                Mocker.GetMock<IQBittorrentBackupReader>().Setup(r => r.Read("/other")).Returns(new List<ExternalTorrent>
                {
                    new ExternalTorrent { Hash = "AAAA", Name = "New", SavePath = folder, Stopped = true, TorrentFile = torrentFile },
                    new ExternalTorrent { Hash = "BBBB", Name = "Have", SavePath = folder, TorrentFile = torrentFile },
                    new ExternalTorrent { Hash = "CCCC", Name = "Far", SavePath = "/not/here/at/all", TorrentFile = torrentFile }
                });

                Mocker.GetMock<IEngineControlService>().Setup(e => e.GetTorrentIds()).Returns(new HashSet<string> { "BBBB" });

                var items = Subject.Import(Options(ExternalInstallKind.QBittorrent));

                items.Select(i => i.Status).Should().Equal(ExternalImportStatus.Add, ExternalImportStatus.Exists, ExternalImportStatus.Skipped);
                items[2].Reason.Should().Contain("can't be reached");

                Mocker.GetMock<IEngineControlService>().Verify(e => e.AdoptTorrentFile("a.torrent", It.IsAny<byte[]>(), folder, true), Times.Once());
                Mocker.GetMock<IEngineControlService>().Verify(e => e.AdoptTorrentFile(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once());
            }
            finally
            {
                System.IO.Directory.Delete(folder, true);
            }
        }

        [Test]
        public void should_refuse_an_unknown_kind()
        {
            Assert.Throws<ArgumentException>(() => ExternalImportOptionsParser.Parse("lidarr", "/x", null, null));
        }
    }
}
