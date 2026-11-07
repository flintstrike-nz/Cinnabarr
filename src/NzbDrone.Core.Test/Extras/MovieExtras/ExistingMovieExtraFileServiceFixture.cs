using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Extras.MovieExtras;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras.MovieExtras
{
    [TestFixture]
    public class ExistingMovieExtraFileServiceFixture : CoreTest<ExistingMovieExtraFileService>
    {
        private Movie _movie;
        private MovieFile _movieFile;
        private List<MovieSubtitleFile> _upsertedSubtitles;
        private List<MovieOtherExtraFile> _upsertedOthers;

        private string P(string name) => Path.Combine(_movie.Path, name);

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                   .With(m => m.Id = 7)
                                   .With(m => m.Path = @"C:\Test\Movies\Movie Title (2022)".AsOsAgnostic())
                                   .Build();

            _movieFile = Builder<MovieFile>.CreateNew()
                                           .With(f => f.Id = 3)
                                           .With(f => f.RelativePath = "Movie Title (2022).mkv")
                                           .Build();

            _upsertedSubtitles = new List<MovieSubtitleFile>();
            _upsertedOthers = new List<MovieOtherExtraFile>();

            Mocker.GetMock<IMovieFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<MovieFile> { _movieFile });

            Mocker.GetMock<IMovieSubtitleFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<MovieSubtitleFile>());

            Mocker.GetMock<IMovieOtherExtraFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<MovieOtherExtraFile>());

            Mocker.GetMock<IMovieSubtitleFileService>()
                  .Setup(s => s.Upsert(It.IsAny<List<MovieSubtitleFile>>()))
                  .Callback<List<MovieSubtitleFile>>(l => _upsertedSubtitles.AddRange(l));

            Mocker.GetMock<IMovieOtherExtraFileService>()
                  .Setup(s => s.Upsert(It.IsAny<List<MovieOtherExtraFile>>()))
                  .Callback<List<MovieOtherExtraFile>>(l => _upsertedOthers.AddRange(l));
        }

        private void Scan(params string[] names)
        {
            Subject.Handle(new MovieScannedEvent(_movie, names.Select(P).ToList()));
        }

        [Test]
        public void should_link_a_subtitle_named_after_the_movie_file_with_its_language()
        {
            Scan("Movie Title (2022).en.srt");

            _upsertedSubtitles.Should().HaveCount(1);
            _upsertedSubtitles[0].MovieId.Should().Be(_movie.Id);
            _upsertedSubtitles[0].MovieFileId.Should().Be(_movieFile.Id);
            _upsertedSubtitles[0].RelativePath.Should().Be("Movie Title (2022).en.srt");
            _upsertedSubtitles[0].Language.Should().Be(NzbDrone.Core.Languages.Language.English);
            _upsertedSubtitles[0].Extension.Should().Be(".srt");
        }

        [Test]
        public void should_link_nfo_and_artwork_named_after_the_movie_file_as_other_extras()
        {
            Scan("Movie Title (2022).nfo", "Movie Title (2022)-poster.jpg");

            _upsertedOthers.Select(o => o.RelativePath).Should().BeEquivalentTo("Movie Title (2022).nfo", "Movie Title (2022)-poster.jpg");
            _upsertedOthers.Should().OnlyContain(o => o.MovieFileId == _movieFile.Id && o.MovieId == _movie.Id);
            _upsertedSubtitles.Should().BeEmpty();
        }

        [Test]
        public void should_give_a_differently_named_subtitle_to_the_only_movie_file()
        {
            Scan("subs.srt");

            _upsertedSubtitles.Should().HaveCount(1);
            _upsertedSubtitles[0].MovieFileId.Should().Be(_movieFile.Id);
        }

        [Test]
        public void should_not_link_a_differently_named_non_subtitle_file()
        {
            Scan("poster.jpg", "folder.jpg");

            _upsertedOthers.Should().BeEmpty();
        }

        [Test]
        public void should_not_link_a_subtitle_to_the_wrong_file_when_there_are_several()
        {
            var other = Builder<MovieFile>.CreateNew()
                                          .With(f => f.Id = 4)
                                          .With(f => f.RelativePath = "Movie Title (2022) - 4K.mkv")
                                          .Build();

            Mocker.GetMock<IMovieFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<MovieFile> { _movieFile, other });

            Scan("Movie Title (2022) - 4K.en.srt", "unrelated.srt");

            _upsertedSubtitles.Should().HaveCount(1);
            _upsertedSubtitles[0].MovieFileId.Should().Be(other.Id);
        }

        [Test]
        public void should_ignore_incomplete_download_files()
        {
            Scan("Movie Title (2022).mkv.!qB", "Movie Title (2022).nfo.part");

            _upsertedOthers.Should().BeEmpty();
        }

        [Test]
        public void should_not_link_files_that_are_already_tracked()
        {
            Mocker.GetMock<IMovieOtherExtraFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<MovieOtherExtraFile>
                           {
                               new MovieOtherExtraFile { Id = 1, MovieId = _movie.Id, MovieFileId = _movieFile.Id, RelativePath = "Movie Title (2022).nfo", Extension = ".nfo" }
                           });

            Scan("Movie Title (2022).nfo");

            _upsertedOthers.Should().BeEmpty();
            Mocker.GetMock<IMovieOtherExtraFileService>().Verify(s => s.DeleteMany(It.Is<IEnumerable<int>>(ids => !ids.Any())), Times.Once());
        }

        [Test]
        public void should_forget_records_of_files_that_are_gone()
        {
            Mocker.GetMock<IMovieOtherExtraFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<MovieOtherExtraFile>
                           {
                               new MovieOtherExtraFile { Id = 11, MovieId = _movie.Id, MovieFileId = _movieFile.Id, RelativePath = "Movie Title (2022).nfo", Extension = ".nfo" }
                           });

            Scan();

            Mocker.GetMock<IMovieOtherExtraFileService>().Verify(s => s.DeleteMany(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 11 }))), Times.Once());
        }
    }
}
