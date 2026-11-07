using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Commands;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class MoveMovieServiceFixture : CoreTest<MoveMovieService>
    {
        private Movie _movie;
        private MoveMovieCommand _command;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>
                .CreateNew()
                .Build();

            _command = new MoveMovieCommand
            {
                MovieId = 1,
                SourcePath = @"C:\Test\Movies\Movie".AsOsAgnostic(),
                DestinationPath = @"C:\Test\Movies2\Movie".AsOsAgnostic()
            };

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(It.IsAny<int>()))
                  .Returns(_movie);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(It.IsAny<string>()))
                  .Returns(true);
        }

        private void GivenFailedMove()
        {
            Mocker.GetMock<IDiskTransferService>()
                  .Setup(s => s.TransferFolder(It.IsAny<string>(), It.IsAny<string>(), TransferMode.Move))
                  .Throws<IOException>();
        }

        [Test]
        public void should_log_error_when_move_throws_an_exception()
        {
            GivenFailedMove();

            Subject.Execute(_command);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_revert_movie_path_on_error()
        {
            GivenFailedMove();

            Subject.Execute(_command);

            ExceptionVerification.ExpectedErrors(1);

            Mocker.GetMock<IMovieService>()
                  .Verify(v => v.UpdateMovie(It.IsAny<Movie>()), Times.Once());
        }

        [Test]
        public void should_use_destination_path()
        {
            Subject.Execute(_command);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFolder(_command.SourcePath, _command.DestinationPath, TransferMode.Move), Times.Once());
        }

        [Test]
        public void should_skip_movie_folder_if_it_does_not_exist()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.Execute(_command);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFolder(_command.SourcePath, _command.DestinationPath, TransferMode.Move), Times.Never());
        }

        [Test]
        public void should_queue_the_file_rename_after_a_successful_move()
        {
            _command.RenameFileIds = new List<int> { 4, 5 };

            Subject.Execute(_command);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.Push(It.Is<RenameMovieFilesCommand>(c => c.MovieId == _movie.Id && c.Files.Count == 2), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void should_not_queue_a_file_rename_when_none_was_asked_for()
        {
            Subject.Execute(_command);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.Push(It.IsAny<RenameMovieFilesCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void should_not_queue_the_file_rename_when_the_move_fails()
        {
            GivenFailedMove();
            _command.RenameFileIds = new List<int> { 4 };

            Subject.Execute(_command);

            ExceptionVerification.ExpectedErrors(1);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.Push(It.IsAny<RenameMovieFilesCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }
    }
}
