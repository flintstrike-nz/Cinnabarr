using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class MergeIntoMovieServiceFixture : CoreTest<MergeIntoMovieService>
    {
        private Movie _movie;
        private string _root;
        private string _file;

        [SetUp]
        public void Setup()
        {
            _root = @"C:\Media\Movies".AsOsAgnostic();
            _file = System.IO.Path.Combine(_root, "Heat.1995.1080p.mkv");
            _movie = new Movie { Id = 3, Path = System.IO.Path.Combine(_root, "Heat (1995)"), MovieMetadata = new MovieMetadata { Title = "Heat" } };

            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(3)).Returns(_movie);
            Mocker.GetMock<IRootFolderService>().Setup(r => r.GetBestRootFolderPath(It.IsAny<string>())).Returns(_root);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(_file)).Returns(true);
        }

        private static ImportResult Result(string path, params string[] errors)
        {
            return new ImportResult(new ImportDecision(new LocalMovie { Path = path }), errors);
        }

        [Test]
        public void should_move_a_loose_file_into_the_movie_and_report_it()
        {
            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Setup(s => s.ProcessPath(_file, ImportMode.Move, _movie, null))
                  .Returns(new List<ImportResult> { Result(_file) });

            var command = new MergeIntoMovieCommand { MovieId = 3, Paths = new List<string> { _file } };
            Subject.Execute(command);

            command.Results.Should().ContainSingle().Which.Merged.Should().BeTrue();
        }

        [Test]
        public void should_say_why_a_file_was_left()
        {
            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Setup(s => s.ProcessPath(_file, ImportMode.Move, _movie, null))
                  .Returns(new List<ImportResult> { Result(_file, "Not an upgrade for existing movie file(s)") });

            var command = new MergeIntoMovieCommand { MovieId = 3, Paths = new List<string> { _file } };
            Subject.Execute(command);

            var result = command.Results.Should().ContainSingle().Subject;
            result.Merged.Should().BeFalse();
            result.Reason.Should().Be("Not an upgrade for existing movie file(s)");
        }

        [Test]
        public void should_not_touch_the_root_folder_the_movie_folder_or_a_missing_path()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_root)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_movie.Path)).Returns(true);

            var command = new MergeIntoMovieCommand { MovieId = 3, Paths = new List<string> { _root, _movie.Path, System.IO.Path.Combine(_root, "Gone") } };
            Subject.Execute(command);

            command.Results.Should().HaveCount(3).And.OnlyContain(r => !r.Merged);
            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Verify(s => s.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), null), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }
    }
}
