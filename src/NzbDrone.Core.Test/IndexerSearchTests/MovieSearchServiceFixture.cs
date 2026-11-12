using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    [TestFixture]
    public class MovieSearchServiceFixture : CoreTest<MovieSearchService>
    {
        private static Movie Movie(int id, bool monitored = true, MovieStatusType availability = MovieStatusType.Announced)
        {
            return new Movie { Id = id, Monitored = monitored, MinimumAvailability = availability, MovieMetadata = new MovieMetadata { Title = $"Movie {id}", Year = 1999 } };
        }

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IVersionSlotService>().Setup(s => s.GetView(It.IsAny<Movie>(), It.IsAny<int>())).Returns<Movie, int>((m, _) => m);
            Mocker.GetMock<ISearchForMovieReleases>()
                  .Setup(s => s.MovieSearch(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .ReturnsAsync(new List<DownloadDecision>());

            Mocker.GetMock<IProcessDownloadDecisions>()
                  .Setup(p => p.ProcessDecisions(It.IsAny<List<DownloadDecision>>()))
                  .ReturnsAsync(new ProcessedDecisions(new List<DownloadDecision>(), new List<DownloadDecision>(), new List<DownloadDecision>()));

            Mocker.GetMock<IQueueService>().Setup(q => q.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue>());
        }

        private void VerifySearched(params int[] ids)
        {
            foreach (var id in ids)
            {
                Mocker.GetMock<ISearchForMovieReleases>().Verify(s => s.MovieSearch(id, It.IsAny<bool>(), false), Times.Once());
            }

            Mocker.GetMock<ISearchForMovieReleases>().Verify(s => s.MovieSearch(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Exactly(ids.Length));
        }

        [Test]
        public void should_search_the_version_asked_for_as_that_version()
        {
            var movie = Movie(1);
            var view = Movie(1);
            view.Slot = new VersionSlot { Id = 4, Label = "4K" };

            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>())).Returns(new List<Movie> { movie });
            Mocker.GetMock<IVersionSlotService>().Setup(s => s.GetView(movie, 4)).Returns(view);
            Mocker.GetMock<ISearchForMovieReleases>()
                  .Setup(s => s.MovieSearch(1, 4, It.IsAny<bool>(), false))
                  .ReturnsAsync(new List<DownloadDecision>());

            Subject.Execute(new MoviesSearchCommand(new List<int> { 1 }) { VersionSlotId = 4, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForMovieReleases>().Verify(s => s.MovieSearch(1, 4, It.IsAny<bool>(), false), Times.Once());
            Mocker.GetMock<ISearchForMovieReleases>().Verify(s => s.MovieSearch(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void a_version_in_the_queue_does_not_stop_the_search_for_another_version_of_the_movie()
        {
            var queuedView = Movie(1);
            queuedView.Slot = new VersionSlot { Id = 4, Label = "4K" };

            Mocker.GetMock<IQueueService>().Setup(q => q.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue> { new NzbDrone.Core.Queue.Queue { Movie = queuedView } });
            Mocker.GetMock<IMovieService>().Setup(s => s.MoviesWithoutFiles(It.IsAny<PagingSpec<Movie>>())).Returns(new PagingSpec<Movie> { Records = new List<Movie> { Movie(1) } });

            Subject.Execute(new MissingMoviesSearchCommand { Trigger = CommandTrigger.Manual });

            VerifySearched(1);
        }

        [Test]
        public void should_search_each_requested_movie()
        {
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>())).Returns(new List<Movie> { Movie(1), Movie(2) });

            Subject.Execute(new MoviesSearchCommand(new List<int> { 1, 2 }) { Trigger = CommandTrigger.Manual });

            VerifySearched(1, 2);
        }

        [Test]
        public void should_skip_unmonitored_movies_in_an_automatic_search()
        {
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>())).Returns(new List<Movie> { Movie(1), Movie(2, monitored: false) });

            Subject.Execute(new MoviesSearchCommand(new List<int> { 1, 2 }) { Trigger = CommandTrigger.Scheduled });

            VerifySearched(1);
        }

        [Test]
        public void should_always_search_when_the_user_asked_for_it()
        {
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>())).Returns(new List<Movie> { Movie(2, monitored: false) });

            Subject.Execute(new MoviesSearchCommand(new List<int> { 2 }) { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForMovieReleases>().Verify(s => s.MovieSearch(2, true, false), Times.Once());
        }

        [Test]
        public void should_search_the_movie_searched_longest_ago_first()
        {
            var order = new List<int>();
            Mocker.GetMock<ISearchForMovieReleases>()
                  .Setup(s => s.MovieSearch(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Callback<int, bool, bool>((id, _, _) => order.Add(id))
                  .ReturnsAsync(new List<DownloadDecision>());

            var recent = Movie(1);
            recent.LastSearchTime = System.DateTime.UtcNow;
            var never = Movie(2);
            var old = Movie(3);
            old.LastSearchTime = System.DateTime.UtcNow.AddDays(-10);

            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>())).Returns(new List<Movie> { recent, never, old });

            Subject.Execute(new MoviesSearchCommand(new List<int> { 1, 2, 3 }) { Trigger = CommandTrigger.Manual });

            order.Should().Equal(2, 3, 1);
        }

        [Test]
        public void should_keep_going_when_one_search_fails()
        {
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>())).Returns(new List<Movie> { Movie(1), Movie(2) });
            Mocker.GetMock<ISearchForMovieReleases>().Setup(s => s.MovieSearch(1, It.IsAny<bool>(), It.IsAny<bool>())).ThrowsAsync(new System.InvalidOperationException("boom"));

            Subject.Execute(new MoviesSearchCommand(new List<int> { 1, 2 }) { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForMovieReleases>().Verify(s => s.MovieSearch(2, It.IsAny<bool>(), false), Times.Once());
            NzbDrone.Test.Common.ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void should_search_missing_monitored_movies_that_are_not_in_the_queue()
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.MoviesWithoutFiles(It.IsAny<PagingSpec<Movie>>()))
                  .Returns(new PagingSpec<Movie> { Records = new List<Movie> { Movie(1), Movie(2) } });
            Mocker.GetMock<IQueueService>().Setup(q => q.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue> { new NzbDrone.Core.Queue.Queue { Movie = Movie(2) } });

            Subject.Execute(new MissingMoviesSearchCommand { Trigger = CommandTrigger.Scheduled });

            VerifySearched(1);
            Mocker.GetMock<IMovieService>().Verify(s => s.MoviesWithoutFiles(It.Is<PagingSpec<Movie>>(p => p.FilterExpressions.Count == 1)), Times.Once());
        }

        [Test]
        public void should_search_cutoff_unmet_movies_that_are_not_in_the_queue()
        {
            Mocker.GetMock<IMovieCutoffService>()
                  .Setup(s => s.MoviesWhereCutoffUnmet(It.IsAny<PagingSpec<Movie>>()))
                  .Returns(new PagingSpec<Movie> { Records = new List<Movie> { Movie(1), Movie(2), Movie(3) } });
            Mocker.GetMock<IQueueService>().Setup(q => q.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue> { new NzbDrone.Core.Queue.Queue { Movie = Movie(1) }, new NzbDrone.Core.Queue.Queue { Movie = Movie(3) } });

            Subject.Execute(new CutoffUnmetMoviesSearchCommand { Trigger = CommandTrigger.Manual });

            VerifySearched(2);
        }

        [Test]
        public void should_grab_what_the_search_decided()
        {
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>())).Returns(new List<Movie> { Movie(1) });
            var decisions = new List<DownloadDecision> { new(new RemoteMovie { Release = new ReleaseInfo() }) };
            Mocker.GetMock<ISearchForMovieReleases>().Setup(s => s.MovieSearch(1, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(decisions);

            Subject.Execute(new MoviesSearchCommand(new List<int> { 1 }) { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<IProcessDownloadDecisions>().Verify(p => p.ProcessDecisions(decisions), Times.Once());
        }
    }
}
