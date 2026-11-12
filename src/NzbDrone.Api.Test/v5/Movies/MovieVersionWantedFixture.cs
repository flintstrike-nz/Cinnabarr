using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.History;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.RootFolders;
using Sonarr.Api.V5.Movies;

namespace NzbDrone.Api.Test.v5.Movies
{
    [TestFixture]
    public class MovieVersionWantedFixture
    {
        private Mock<IVersionSlotService> _slots;
        private Movie _view;

        [SetUp]
        public void Setup()
        {
            _view = new Movie
            {
                Id = 3,
                QualityProfileId = 4,
                MovieMetadata = new MovieMetadata { Title = "Heat", Year = 1995 }
            };

            _view.Slot = new VersionSlot { Id = 9, MovieId = 3, Label = "UHD", QualityProfileId = 4 };

            _slots = new Mock<IVersionSlotService>();
            _slots.Setup(s => s.GetMissingViews()).Returns(new List<Movie> { _view });
            _slots.Setup(s => s.GetCutoffUnmetViews()).Returns(new List<Movie> { _view });
        }

        [Test]
        public void should_list_the_missing_and_cutoff_unmet_versions()
        {
            var controller = new MovieWantedController(Mock.Of<IMovieService>(), Mock.Of<IMovieCutoffService>(), Mock.Of<IRootFolderService>(), _slots.Object);

            foreach (var result in new[] { controller.GetMissingVersions().Value, controller.GetCutoffUnmetVersions().Value })
            {
                var item = result.Single();

                item.MovieId.Should().Be(3);
                item.MovieTitle.Should().Be("Heat");
                item.Year.Should().Be(1995);
                item.VersionSlotId.Should().Be(9);
                item.VersionLabel.Should().Be("UHD");
                item.QualityProfileId.Should().Be(4);
            }
        }

        [Test]
        public void should_label_the_history_of_a_version_and_leave_the_default_unlabelled()
        {
            var history = new Mock<IMovieHistoryService>();
            history.Setup(h => h.GetByMovieId(3)).Returns(new List<MovieHistory>
            {
                new MovieHistory { Id = 1, MovieId = 3, SourceTitle = "a", Data = new Dictionary<string, string> { [MovieHistory.VERSION_SLOT_ID] = "9" } },
                new MovieHistory { Id = 2, MovieId = 3, SourceTitle = "b", Data = new Dictionary<string, string>() }
            });

            var movies = new Mock<IMovieService>();
            movies.Setup(m => m.GetMovies(It.IsAny<IEnumerable<int>>())).Returns(new List<Movie> { _view });

            _slots.Setup(s => s.GetByMovie(3)).Returns(new List<VersionSlot> { new VersionSlot { Id = 9, MovieId = 3, Label = "UHD" } });

            var controller = new MovieHistoryController(history.Object, movies.Object, _slots.Object);

            var items = controller.GetMovieHistory(3).Value;

            items.Single(i => i.Id == 1).VersionLabel.Should().Be("UHD");
            items.Single(i => i.Id == 2).VersionLabel.Should().BeNull();
            items.Single(i => i.Id == 2).VersionSlotId.Should().Be(0);
        }
    }
}
