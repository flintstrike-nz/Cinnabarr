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
