using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Reorganise;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ReorganiseTests
{
    [TestFixture]
    public class ReorganiseServiceFixture : CoreTest<ReorganiseService>
    {
        private Movie _movie;
        private MovieFile _file;
        private List<ReorganiseJournalEntry> _entries;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie { Id = 4, Path = @"C:\Movies\Heat".AsOsAgnostic(), MovieMetadata = new MovieMetadata { Title = "Heat" } };
            _file = new MovieFile { Id = 9, MovieId = 4, RelativePath = "Heat new.mkv" };
            _entries = new List<ReorganiseJournalEntry>();

            Mocker.GetMock<IMovieService>().Setup(m => m.GetAllMovies()).Returns(new List<Movie> { _movie });
            Mocker.GetMock<IMovieService>().Setup(m => m.GetMovie(4)).Returns(_movie);
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(new List<Series>());
            Mocker.GetMock<IMovieFileService>().Setup(m => m.GetMovie(9)).Returns(_file);

            Mocker.GetMock<IReorganiseJournalRepository>().Setup(j => j.Insert(It.IsAny<ReorganiseJournalEntry>())).Returns(AddEntry);
            Mocker.GetMock<IReorganiseJournalRepository>().Setup(j => j.All()).Returns(() => _entries);
            Mocker.GetMock<IReorganiseJournalRepository>().Setup(j => j.GetByRun(It.IsAny<string>())).Returns((string id) => _entries.Where(e => e.RunId == id).ToList());

            Mocker.GetMock<ILooseMediaService>().Setup(l => l.Find(It.IsAny<string>())).Returns(new List<ReorganisePreviewItem>());
            Mocker.GetMock<IRenameMovieFileService>().Setup(r => r.GetRenamePreviews(It.IsAny<List<int>>())).Returns(new List<RenameMovieFilePreview>());
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_movie.Path)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(Path.Combine(_movie.Path, "Heat old.mkv"))).Returns(true);
        }

        private ReorganiseJournalEntry AddEntry(ReorganiseJournalEntry entry)
        {
            entry.Id = _entries.Count + 1;
            _entries.Add(entry);

            return entry;
        }

        private void GivenPreview()
        {
            Mocker.GetMock<IRenameMovieFileService>()
                  .Setup(r => r.GetRenamePreviews(It.IsAny<List<int>>()))
                  .Returns(new List<RenameMovieFilePreview> { new RenameMovieFilePreview { MovieId = 4, MovieFileId = 9, ExistingPath = Path.Combine(_movie.Path, "Heat old.mkv"), NewPath = Path.Combine(_movie.Path, "Heat new.mkv") } });
        }

        [Test]
        public void should_preview_the_files_that_do_not_follow_the_naming_format()
        {
            GivenPreview();

            var item = Subject.Preview().Single();

            item.MediaType.Should().Be("movie");
            item.Title.Should().Be("Heat");
            item.FileId.Should().Be(9);
            item.To.Should().EndWith("Heat new.mkv");
        }

        [Test]
        public void should_journal_the_renames_of_a_run_and_nothing_outside_one()
        {
            GivenPreview();

            // A rename outside a run is not journaled.
            Subject.Handle(new MovieFileRenamedEvent(_movie, _file, Path.Combine(_movie.Path, "Heat other.mkv")));
            _entries.Should().BeEmpty();

            // The movie's renames inside a run are.
            Mocker.GetMock<IExecute<RenameMovieFilesCommand>>()
                  .Setup(e => e.Execute(It.IsAny<RenameMovieFilesCommand>()))
                  .Callback(() => Subject.Handle(new MovieFileRenamedEvent(_movie, _file, Path.Combine(_movie.Path, "Heat old.mkv"))));

            Subject.Execute(new ReorganiseLibraryCommand());

            var entry = _entries.Single();

            entry.MediaType.Should().Be("movie");
            entry.FileId.Should().Be(9);
            entry.FromPath.Should().EndWith("Heat old.mkv");
            entry.ToPath.Should().EndWith("Heat new.mkv");
            Subject.GetRuns().Single().Files.Should().Be(1);
        }

        [Test]
        public void should_keep_going_when_one_movie_cannot_be_renamed()
        {
            GivenPreview();
            Mocker.GetMock<IExecute<RenameMovieFilesCommand>>().Setup(e => e.Execute(It.IsAny<RenameMovieFilesCommand>())).Throws(new IOException("locked"));

            Subject.Execute(new ReorganiseLibraryCommand());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_say_why_a_file_on_a_drive_that_cannot_be_reached_is_not_renamed()
        {
            GivenPreview();
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_movie.Path)).Returns(false);

            var item = Subject.Preview().Single();

            item.Problem.Should().Contain("can't be reached");
        }

        [Test]
        public void should_say_why_a_file_whose_new_name_is_taken_is_not_renamed()
        {
            GivenPreview();
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(Path.Combine(_movie.Path, "Heat new.mkv"))).Returns(true);

            var item = Subject.Preview().Single();

            item.Problem.Should().Contain("already has the name");
        }

        [Test]
        public void should_say_why_a_file_that_is_gone_is_not_renamed()
        {
            GivenPreview();
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(Path.Combine(_movie.Path, "Heat old.mkv"))).Returns(false);

            Subject.Preview().Single().Problem.Should().Contain("isn't there any more");
        }

        [Test]
        public void should_not_rename_two_files_to_the_same_name()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(Path.Combine(_movie.Path, "Heat new.mkv"))).Returns(false);
            Mocker.GetMock<IRenameMovieFileService>()
                  .Setup(r => r.GetRenamePreviews(It.IsAny<List<int>>()))
                  .Returns(new List<RenameMovieFilePreview>
                  {
                      new RenameMovieFilePreview { MovieId = 4, MovieFileId = 9, ExistingPath = "Heat old.mkv", NewPath = "Heat new.mkv" },
                      new RenameMovieFilePreview { MovieId = 4, MovieFileId = 10, ExistingPath = "Heat copy.mkv", NewPath = "Heat new.mkv" }
                  });

            var items = Subject.Preview();

            items.Should().HaveCount(2);
            items.Should().OnlyContain(i => i.Problem != null);

            Subject.Execute(new ReorganiseLibraryCommand());

            Mocker.GetMock<IExecute<RenameMovieFilesCommand>>().Verify(e => e.Execute(It.IsAny<RenameMovieFilesCommand>()), Times.Never());
        }

        [Test]
        public void should_only_look_in_the_folder_asked_for()
        {
            GivenPreview();

            Subject.Preview(@"C:\Movies\Alien".AsOsAgnostic()).Should().BeEmpty();
            Subject.Preview(@"C:\Movies".AsOsAgnostic()).Should().HaveCount(1);

            Mocker.GetMock<ILooseMediaService>().Verify(l => l.Find(@"C:\Movies".AsOsAgnostic()), Times.Once());
        }

        [Test]
        public void should_list_loose_files_and_move_the_ones_that_can_go_in()
        {
            var loose = new List<ReorganisePreviewItem>
            {
                new ReorganisePreviewItem { Kind = ReorganiseKinds.Import, MediaType = ReorganiseMediaTypes.Movie, MediaId = 4, Title = "Heat", From = @"C:\Movies\Heat.1995.mkv".AsOsAgnostic(), To = Path.Combine(_movie.Path, "Heat (1995).mkv") },
                new ReorganisePreviewItem { Kind = ReorganiseKinds.Import, MediaType = ReorganiseMediaTypes.Movie, From = @"C:\Movies\Home Video.mkv".AsOsAgnostic(), Problem = "Its name doesn't match a movie in the library" }
            };

            Mocker.GetMock<ILooseMediaService>().Setup(l => l.Find(It.IsAny<string>())).Returns(loose);

            Subject.Preview().Should().HaveCount(2);

            Subject.Execute(new ReorganiseLibraryCommand());

            Mocker.GetMock<ILooseMediaService>().Verify(l => l.Import(It.Is<List<ReorganisePreviewItem>>(i => i.Count == 1 && i[0].MediaId == 4)), Times.Once());
        }

        [Test]
        public void should_rename_a_movie_folder_only_when_asked_and_journal_it()
        {
            var target = @"C:\Movies\Heat (1995)".AsOsAgnostic();

            Mocker.GetMock<IRootFolderService>().Setup(r => r.GetBestRootFolderPath(_movie.Path)).Returns(@"C:\Movies".AsOsAgnostic());
            Mocker.GetMock<NzbDrone.Core.Organizer.IBuildMovieFolderName>().Setup(b => b.GetMovieFolder(_movie)).Returns("Heat (1995)");
            Mocker.GetMock<IExecute<NzbDrone.Core.Movies.Commands.MoveMovieCommand>>()
                  .Setup(e => e.Execute(It.IsAny<NzbDrone.Core.Movies.Commands.MoveMovieCommand>()))
                  .Callback(() => Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(target)).Returns(true));

            Subject.Preview().Should().BeEmpty();

            var item = Subject.Preview(null, true).Single();
            item.Kind.Should().Be(ReorganiseKinds.Folder);
            item.To.Should().Be(target);

            Subject.Execute(new ReorganiseLibraryCommand { IncludeFolders = true });

            _movie.Path.Should().Be(target);
            _entries.Single().MediaType.Should().Be(ReorganiseMediaTypes.MovieFolder);
        }

        private ReorganiseJournalEntry GivenRun()
        {
            var entry = new ReorganiseJournalEntry
            {
                Id = 1,
                RunId = "run",
                MediaType = "movie",
                MediaId = 4,
                FileId = 9,
                FromPath = Path.Combine(_movie.Path, "Heat old.mkv"),
                ToPath = Path.Combine(_movie.Path, "Heat new.mkv"),
                Applied = DateTime.UtcNow
            };

            _entries.Add(entry);

            return entry;
        }

        [Test]
        public void should_undo_a_run_by_moving_the_file_back_and_fixing_its_record()
        {
            var entry = GivenRun();
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(entry.ToPath)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(entry.FromPath)).Returns(false);

            Subject.Execute(new ReorganiseUndoCommand { RunId = "run" });

            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(entry.ToPath, entry.FromPath, false), Times.Once());
            _file.RelativePath.Should().Be("Heat old.mkv");
            Mocker.GetMock<IMovieFileService>().Verify(m => m.Update(_file), Times.Once());
            entry.Undone.Should().NotBeNull();
        }

        [Test]
        public void should_not_undo_a_rename_when_the_file_has_moved_on_or_the_old_name_is_taken()
        {
            var entry = GivenRun();

            // The renamed file is gone.
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(entry.ToPath)).Returns(false);
            Subject.Execute(new ReorganiseUndoCommand { RunId = "run" });

            // The old name is taken.
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(entry.ToPath)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(entry.FromPath)).Returns(true);
            Subject.Execute(new ReorganiseUndoCommand { RunId = "run" });

            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            entry.Undone.Should().BeNull();
            ExceptionVerification.ExpectedWarns(2);
        }
    }
}
