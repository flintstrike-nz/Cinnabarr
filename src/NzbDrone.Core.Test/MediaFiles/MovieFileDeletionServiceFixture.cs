using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class MovieFileDeletionServiceFixture : CoreTest<MovieFileDeletionService>
    {
        private const string RootFolder = @"C:\Test\Movies";
        private Movie _movie;
        private MovieFile _movieFile;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                     .With(s => s.Path = Path.Combine(RootFolder, "Movie Title"))
                                     .Build();

            _movieFile = Builder<MovieFile>.CreateNew()
                                               .With(f => f.RelativePath = "Some SubFolder")
                                               .With(f => f.Path = Path.Combine(_movie.Path, "Some SubFolder"))
                                               .Build();

            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.GetBestRootFolderPath(_movie.Path))
                  .Returns(RootFolder);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetParentFolder(_movie.Path))
                  .Returns(RootFolder);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetParentFolder(_movieFile.Path))
                  .Returns(_movie.Path);
        }

        private void GivenRootFolderExists()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(RootFolder))
                  .Returns(true);
        }

        private void GivenRootFolderHasFolders()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetDirectories(RootFolder))
                  .Returns(new[] { _movie.Path });
        }

        private void GivenMovieFolderExists()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(_movie.Path))
                  .Returns(true);
        }

        [Test]
        public void should_throw_if_root_folder_does_not_exist()
        {
            Assert.Throws<NzbDroneClientException>(() => Subject.DeleteMovieFile(_movie, _movieFile));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_should_throw_if_root_folder_is_empty()
        {
            GivenRootFolderExists();

            Assert.Throws<NzbDroneClientException>(() => Subject.DeleteMovieFile(_movie, _movieFile));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_delete_from_db_if_movie_folder_does_not_exist()
        {
            GivenRootFolderExists();
            GivenRootFolderHasFolders();

            Subject.DeleteMovieFile(_movie, _movieFile);

            Mocker.GetMock<IMovieFileService>().Verify(v => v.Delete(_movieFile, DeleteMediaFileReason.Manual), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(_movieFile.Path, It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_delete_from_db_if_movie_file_does_not_exist()
        {
            GivenRootFolderExists();
            GivenRootFolderHasFolders();
            GivenMovieFolderExists();

            Subject.DeleteMovieFile(_movie, _movieFile);

            Mocker.GetMock<IMovieFileService>().Verify(v => v.Delete(_movieFile, DeleteMediaFileReason.Manual), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(_movieFile.Path, It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_delete_from_disk_and_db_if_movie_file_exists()
        {
            GivenRootFolderExists();
            GivenRootFolderHasFolders();
            GivenMovieFolderExists();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(_movieFile.Path))
                  .Returns(true);

            Subject.DeleteMovieFile(_movie, _movieFile);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(_movieFile.Path, "Movie Title"), Times.Once());
            Mocker.GetMock<IMovieFileService>().Verify(v => v.Delete(_movieFile, DeleteMediaFileReason.Manual), Times.Once());
        }

        [Test]
        public void should_handle_error_deleting_movie_file()
        {
            GivenRootFolderExists();
            GivenRootFolderHasFolders();
            GivenMovieFolderExists();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(_movieFile.Path))
                  .Returns(true);

            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(s => s.DeleteFile(_movieFile.Path, "Movie Title"))
                  .Throws(new IOException());

            Assert.Throws<NzbDroneClientException>(() => Subject.DeleteMovieFile(_movie, _movieFile));

            ExceptionVerification.ExpectedErrors(1);
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(_movieFile.Path, "Movie Title"), Times.Once());
            Mocker.GetMock<IMovieFileService>().Verify(v => v.Delete(_movieFile, DeleteMediaFileReason.Manual), Times.Never());
        }

        [Test]
        public void should_send_the_movie_folder_to_the_recycle_bin_when_deleted_with_files()
        {
            GivenMovieFolderExists();
            Mocker.GetMock<IMovieService>().Setup(s => s.AllMoviePaths()).Returns(new Dictionary<int, string>());

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _movie }, true, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(_movie.Path), Times.Once());
        }

        [Test]
        public void should_leave_the_folder_alone_when_deleted_without_files()
        {
            GivenMovieFolderExists();

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _movie }, false, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_delete_a_folder_another_movie_still_uses()
        {
            GivenMovieFolderExists();
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AllMoviePaths())
                  .Returns(new Dictionary<int, string> { { 99, _movie.Path } });

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _movie }, true, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
