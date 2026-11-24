using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport.Manual;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Reorganise;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ReorganiseTests
{
    [TestFixture]
    public class LooseMediaServiceFixture : CoreTest<LooseMediaService>
    {
        private string _tvRoot;
        private string _movieRoot;

        [SetUp]
        public void Setup()
        {
            _tvRoot = @"C:\TV".AsOsAgnostic();
            _movieRoot = @"C:\Movies".AsOsAgnostic();

            Mocker.GetMock<IRootFolderService>().Setup(r => r.All()).Returns(new List<RootFolder>
            {
                new RootFolder { Id = 1, Path = _tvRoot, Library = LibraryType.Series },
                new RootFolder { Id = 2, Path = _movieRoot, Library = LibraryType.Movies }
            });

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeriesPaths()).Returns(new Dictionary<int, string> { { 1, Path.Combine(_tvRoot, "Shoresy") } });
            Mocker.GetMock<IMovieService>().Setup(m => m.AllMoviePaths()).Returns(new Dictionary<int, string> { { 1, Path.Combine(_movieRoot, "Heat (1995)") } });

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_tvRoot)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_movieRoot)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetDirectories(It.IsAny<string>())).Returns(System.Array.Empty<string>());
            Mocker.GetMock<IDiskScanService>().Setup(d => d.GetVideoFiles(It.IsAny<string>(), It.IsAny<bool>())).Returns(System.Array.Empty<string>());
            Mocker.GetMock<IDiskScanService>().Setup(d => d.FilterPaths(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                  .Returns((string basePath, IEnumerable<string> paths, bool filterExtras) => paths.ToList());
            Mocker.GetMock<IManualImportService>().Setup(m => m.GetMediaFiles(It.IsAny<string>(), null, null, true)).Returns(new List<ManualImportItem>());
        }

        [Test]
        public void should_say_a_root_folder_that_cannot_be_reached_cannot_be_reorganised()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_tvRoot)).Returns(false);

            var item = Subject.Find(null).Single();

            item.From.Should().Be(_tvRoot);
            item.Problem.Should().Contain("can't be reached");
        }

        [Test]
        public void should_not_look_inside_library_folders()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetDirectories(_tvRoot)).Returns(new[] { Path.Combine(_tvRoot, "Shoresy") });

            Subject.Find(null).Should().BeEmpty();

            Mocker.GetMock<IManualImportService>().Verify(m => m.GetMediaFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_say_why_a_loose_episode_cannot_go_into_a_series()
        {
            var file = Path.Combine(_tvRoot, "Home.Video.S01E01.mkv");

            Mocker.GetMock<IDiskScanService>().Setup(d => d.GetVideoFiles(_tvRoot, false)).Returns(new[] { file });
            Mocker.GetMock<IManualImportService>().Setup(m => m.GetMediaFiles(file, null, null, true)).Returns(new List<ManualImportItem> { new ManualImportItem { Path = file } });

            var item = Subject.Find(null).Single();

            item.From.Should().Be(file);
            item.Problem.Should().Contain("doesn't match a series");
            item.EpisodeImport.Should().BeNull();
        }

        [Test]
        public void should_plan_to_move_a_loose_episode_into_its_series()
        {
            var file = Path.Combine(_tvRoot, "Shoresy.S01E01.mkv");
            var series = new Series { Id = 1, Title = "Shoresy", Path = Path.Combine(_tvRoot, "Shoresy") };

            Mocker.GetMock<IDiskScanService>().Setup(d => d.GetVideoFiles(_tvRoot, false)).Returns(new[] { file });
            Mocker.GetMock<IManualImportService>().Setup(m => m.GetMediaFiles(file, null, null, true)).Returns(new List<ManualImportItem>
            {
                new ManualImportItem { Path = file, Series = series, Episodes = new List<Episode> { new Episode { Id = 3 } } }
            });

            var item = Subject.Find(null).Single();

            item.Problem.Should().BeNull();
            item.MediaId.Should().Be(1);
            item.EpisodeImport.Should().NotBeNull();
        }

        [Test]
        public void should_say_why_a_loose_movie_file_cannot_go_into_a_movie()
        {
            var file = Path.Combine(_movieRoot, "Home Video.mkv");

            Mocker.GetMock<IDiskScanService>().Setup(d => d.GetVideoFiles(_movieRoot, false)).Returns(new[] { file });

            var item = Subject.Find(null).Single();

            item.MediaType.Should().Be(ReorganiseMediaTypes.Movie);
            item.Problem.Should().Contain("doesn't match a movie");
            Mocker.GetMock<IMovieParsingService>().Verify(p => p.GetMovie("Home Video"), Times.Once());
        }

        [Test]
        public void should_only_look_in_the_folder_asked_for()
        {
            var folder = Path.Combine(_tvRoot, "Shoresy.S01.1080p");

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(folder)).Returns(true);

            Subject.Find(folder);

            Mocker.GetMock<IManualImportService>().Verify(m => m.GetMediaFiles(folder, null, null, true), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.GetDirectories(_tvRoot), Times.Never());
        }
    }
}
