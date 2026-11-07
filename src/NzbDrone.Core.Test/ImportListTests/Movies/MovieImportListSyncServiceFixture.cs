using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Movies;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests.Movies
{
    [TestFixture]
    public class MovieImportListSyncServiceFixture : CoreTest<MovieImportListSyncService>
    {
        private ImportListDefinition _list;
        private List<Movie> _added;

        [SetUp]
        public void Setup()
        {
            _list = new ImportListDefinition
            {
                Id = 1,
                EnableAutomaticAdd = true,
                ShouldMonitor = Tv.MonitorTypes.All,
                SearchForMissingEpisodes = true,
                QualityProfileId = 4,
                RootFolderPath = @"C:\Movies",
                Tags = new HashSet<int> { 9 }
            };

            _added = new List<Movie>();

            Mocker.GetMock<IImportListFactory>().Setup(f => f.All()).Returns(new List<ImportListDefinition> { _list });

            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), true))
                  .Returns<List<Movie>, bool>((m, _) =>
                  {
                      _added.AddRange(m);
                      return m;
                  });
        }

        private static ImportListMovieInfo Item(int tmdb, string imdb = null) =>
            new ImportListMovieInfo { ImportListId = 1, Title = "M" + tmdb, Year = 2000, TmdbId = tmdb, ImdbId = imdb };

        [Test]
        public void should_add_a_new_movie_with_the_list_settings()
        {
            Subject.Process(new List<ImportListMovieInfo> { Item(603) }).Should().Be(1);

            _added.Should().HaveCount(1);
            _added[0].TmdbId.Should().Be(603);
            _added[0].Monitored.Should().BeTrue();
            _added[0].QualityProfileId.Should().Be(4);
            _added[0].RootFolderPath.Should().Be(@"C:\Movies");
            _added[0].Tags.Should().BeEquivalentTo(new[] { 9 });
            _added[0].AddOptions.SearchForMovie.Should().BeTrue();
            _added[0].AddOptions.AddMethod.Should().Be(AddMovieMethod.List);
        }

        [Test]
        public void should_add_unmonitored_when_the_list_does_not_monitor()
        {
            _list.ShouldMonitor = Tv.MonitorTypes.None;

            Subject.Process(new List<ImportListMovieInfo> { Item(603) });

            _added[0].Monitored.Should().BeFalse();
            _added[0].AddOptions.Monitor.Should().Be(MonitorTypes.None);
        }

        [Test]
        public void should_skip_movies_already_in_the_library()
        {
            Mocker.GetMock<IMovieService>().Setup(s => s.FindByTmdbId(603)).Returns(new Movie { Id = 5 });

            Subject.Process(new List<ImportListMovieInfo> { Item(603) }).Should().Be(0);

            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_add_a_movie_offered_by_two_lists_once_with_both_tag_sets()
        {
            var second = new ImportListDefinition { Id = 2, EnableAutomaticAdd = true, RootFolderPath = @"C:\Movies", QualityProfileId = 4, Tags = new HashSet<int> { 10 } };
            Mocker.GetMock<IImportListFactory>().Setup(f => f.All()).Returns(new List<ImportListDefinition> { _list, second });

            var other = Item(603);
            other.ImportListId = 2;

            Subject.Process(new List<ImportListMovieInfo> { Item(603), other });

            _added.Should().HaveCount(1);
            _added[0].Tags.Should().BeEquivalentTo(new[] { 9, 10 });
        }

        [Test]
        public void should_map_an_imdb_id_to_a_tmdb_id()
        {
            Mocker.GetMock<IProvideMovieInfo>().Setup(s => s.GetMovieByImdbId("tt0133093")).Returns(new MovieMetadata { TmdbId = 603 });

            Subject.Process(new List<ImportListMovieInfo> { Item(0, "tt0133093") });

            _added.Single().TmdbId.Should().Be(603);
        }

        [Test]
        public void should_skip_an_imdb_id_that_cannot_be_found()
        {
            Mocker.GetMock<IProvideMovieInfo>().Setup(s => s.GetMovieByImdbId("tt1")).Throws(new MovieNotFoundException(0));

            Subject.Process(new List<ImportListMovieInfo> { Item(0, "tt1") }).Should().Be(0);
        }

        [Test]
        public void should_ignore_lists_without_automatic_add()
        {
            _list.EnableAutomaticAdd = false;

            Subject.Process(new List<ImportListMovieInfo> { Item(603) }).Should().Be(0);
        }

        [Test]
        public void should_tag_existing_movies_when_the_list_asks()
        {
            _list.TagExisting = TagExistingType.Add;
            var existing = new Movie { Id = 5 };
            Mocker.GetMock<IMovieService>().Setup(s => s.FindByTmdbId(603)).Returns(existing);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(5)).Returns(existing);

            Subject.Process(new List<ImportListMovieInfo> { Item(603) });

            existing.Tags.Should().Contain(9);
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(existing), Times.Once());
        }

        [Test]
        public void should_read_a_custom_list_in_either_key_style()
        {
            var movies = MovieCustomImport.Parse("[{\"title\":\"A\",\"tmdb_id\":603},{\"title\":\"B\",\"imdbId\":\"tt1\",\"year\":1999},{\"tmdbId\":7}]");

            movies.Select(m => m.TmdbId).Should().Equal(603, 0, 7);
            movies[1].ImdbId.Should().Be("tt1");
            movies[1].Year.Should().Be(1999);
            movies[2].Title.Should().Be("TmdbId: 7");
        }
    }
}
