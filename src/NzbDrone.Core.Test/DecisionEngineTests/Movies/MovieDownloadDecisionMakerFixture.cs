using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Movies;
using NzbDrone.Core.DecisionEngine.Movies.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.DecisionEngineTests.Movies
{
    [TestFixture]
    public class MovieDownloadDecisionMakerFixture : CoreTest<MovieDownloadDecisionMaker>
    {
        private Movie _matrix;
        private List<ReleaseInfo> _reports;
        private Mock<IMovieDecisionEngineSpecification> _pass;
        private Mock<IMovieDecisionEngineSpecification> _fail;

        [SetUp]
        public void SetUp()
        {
            _matrix = new Movie { Id = 1, MovieMetadata = new MovieMetadata { TmdbId = 603, Title = "The Matrix", Year = 1999 } };

            _reports = new List<ReleaseInfo>
            {
                new ReleaseInfo { Title = "The.Matrix.1999.1080p.BluRay.x264-GRP", Size = 10.Gigabytes(), DownloadUrl = "http://x/1" }
            };

            _pass = Spec(SpecificationPriority.Default, DownloadSpecDecision.Accept());
            _fail = Spec(SpecificationPriority.Default, DownloadSpecDecision.Reject(DownloadRejectionReason.MinimumAge, "Too new"));

            Mocker.GetMock<IVersionSlotService>()
                .Setup(v => v.GetByMovie(It.IsAny<int>()))
                .Returns(new List<VersionSlot>());

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(c => c.ParseCustomFormat(It.IsAny<RemoteMovie>(), It.IsAny<long>()))
                .Returns(new List<CustomFormat>());

            Mocker.GetMock<IMovieParsingService>()
                .Setup(p => p.Map(It.IsAny<ParsedMovieInfo>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Movie>()))
                .Returns((ParsedMovieInfo info, string imdb, int tmdb, Movie searched) => new RemoteMovie { ParsedMovieInfo = info, Movie = _matrix, Languages = info.Languages });
        }

        private static Mock<IMovieDecisionEngineSpecification> Spec(SpecificationPriority priority, DownloadSpecDecision result)
        {
            var spec = new Mock<IMovieDecisionEngineSpecification>();
            spec.SetupGet(s => s.Priority).Returns(priority);
            spec.SetupGet(s => s.Type).Returns(RejectionType.Permanent);
            spec.Setup(s => s.IsSatisfiedBy(It.IsAny<RemoteMovie>(), It.IsAny<MovieReleaseDecisionInformation>())).Returns(result);

            return spec;
        }

        private void GivenSpecs(params Mock<IMovieDecisionEngineSpecification>[] specs)
        {
            Mocker.SetConstant<IEnumerable<IMovieDecisionEngineSpecification>>(specs.Select(s => s.Object).ToList());
        }

        [Test]
        public void should_approve_a_release_every_spec_accepts()
        {
            GivenSpecs(_pass);

            var decision = Subject.GetRssDecision(_reports).Single();

            decision.Approved.Should().BeTrue();
            decision.RemoteMovie.Movie.Should().Be(_matrix);
            decision.RemoteMovie.Release.Should().Be(_reports[0]);
            decision.RemoteMovie.ReleaseSource.Should().Be(ReleaseSourceType.Rss);
            decision.RemoteEpisode.Should().BeNull();
        }

        private void GivenAVersion(int slotId)
        {
            Mocker.GetMock<IVersionSlotService>()
                .Setup(v => v.GetByMovie(_matrix.Id))
                .Returns(new List<VersionSlot> { new VersionSlot { Id = slotId, MovieId = _matrix.Id, Label = "UHD", QualityProfileId = 2, Monitored = true } });

            Mocker.GetMock<IVersionSlotService>()
                .Setup(v => v.GetView(_matrix, slotId))
                .Returns(() =>
                {
                    var view = _matrix.ShallowCopy();
                    view.Slot = new VersionSlot { Id = slotId, MovieId = _matrix.Id, Label = "UHD", QualityProfileId = 2, Monitored = true };

                    return view;
                });
        }

        [Test]
        public void should_also_decide_an_rss_release_for_each_version_of_the_movie()
        {
            GivenSpecs(_pass);
            GivenAVersion(7);

            var decisions = Subject.GetRssDecision(_reports);

            decisions.Should().HaveCount(2);
            decisions[0].RemoteMovie.Movie.VersionSlotId.Should().Be(0);
            decisions[1].RemoteMovie.Movie.VersionSlotId.Should().Be(7);
            decisions[1].RemoteMovie.Movie.Id.Should().Be(_matrix.Id);
            decisions[1].RemoteMovie.Release.Should().Be(_reports[0]);
            decisions[1].Approved.Should().BeTrue();
        }

        [Test]
        public void should_not_decide_a_pushed_or_searched_release_for_the_versions()
        {
            GivenSpecs(_pass);
            GivenAVersion(7);

            Subject.GetRssDecision(_reports, true).Should().HaveCount(1);
            Subject.GetSearchDecision(_reports, _matrix).Should().HaveCount(1);
        }

        [Test]
        public void should_reject_for_a_version_on_its_own_when_the_specs_say_so()
        {
            var onlyDefault = new Mock<IMovieDecisionEngineSpecification>();
            onlyDefault.SetupGet(s => s.Priority).Returns(SpecificationPriority.Default);
            onlyDefault.SetupGet(s => s.Type).Returns(RejectionType.Permanent);
            onlyDefault.Setup(s => s.IsSatisfiedBy(It.IsAny<RemoteMovie>(), It.IsAny<MovieReleaseDecisionInformation>()))
                .Returns((RemoteMovie m, MovieReleaseDecisionInformation i) => m.Movie.VersionSlotId == 0
                    ? DownloadSpecDecision.Accept()
                    : DownloadSpecDecision.Reject(DownloadRejectionReason.QualityNotWanted, "Not wanted"));
            GivenSpecs(onlyDefault);
            GivenAVersion(7);

            var decisions = Subject.GetRssDecision(_reports);

            decisions[0].Approved.Should().BeTrue();
            decisions[1].Approved.Should().BeFalse();
        }

        [Test]
        public void should_reject_with_the_specs_reason()
        {
            GivenSpecs(_pass, _fail);

            var decision = Subject.GetRssDecision(_reports).Single();

            decision.Approved.Should().BeFalse();
            decision.Rejections.Single().Reason.Should().Be(DownloadRejectionReason.MinimumAge);
        }

        [Test]
        public void should_not_run_later_priority_specs_after_an_earlier_one_rejects()
        {
            var early = Spec(SpecificationPriority.Default, DownloadSpecDecision.Reject(DownloadRejectionReason.MinimumAge, "Early"));
            var late = Spec(SpecificationPriority.Disk, DownloadSpecDecision.Accept());
            GivenSpecs(early, late);

            Subject.GetRssDecision(_reports);

            late.Verify(s => s.IsSatisfiedBy(It.IsAny<RemoteMovie>(), It.IsAny<MovieReleaseDecisionInformation>()), Times.Never());
        }

        [Test]
        public void should_reject_a_release_that_matches_no_movie()
        {
            GivenSpecs(_pass);

            Mocker.GetMock<IMovieParsingService>()
                .Setup(p => p.Map(It.IsAny<ParsedMovieInfo>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Movie>()))
                .Returns((ParsedMovieInfo info, string imdb, int tmdb, Movie searched) => new RemoteMovie { ParsedMovieInfo = info });

            var decision = Subject.GetRssDecision(_reports).Single();

            decision.Rejections.Single().Reason.Should().Be(DownloadRejectionReason.UnknownMovie);
            _pass.Verify(s => s.IsSatisfiedBy(It.IsAny<RemoteMovie>(), It.IsAny<MovieReleaseDecisionInformation>()), Times.Never());
        }

        [Test]
        public void should_turn_a_spec_exception_into_a_rejection()
        {
            var broken = new Mock<IMovieDecisionEngineSpecification>();
            broken.SetupGet(s => s.Priority).Returns(SpecificationPriority.Default);
            broken.SetupGet(s => s.Type).Returns(RejectionType.Permanent);
            broken.Setup(s => s.IsSatisfiedBy(It.IsAny<RemoteMovie>(), It.IsAny<MovieReleaseDecisionInformation>())).Throws(new InvalidOperationException("boom"));
            GivenSpecs(broken);

            var decision = Subject.GetRssDecision(_reports).Single();

            decision.Rejections.Single().Reason.Should().Be(DownloadRejectionReason.DecisionError);
            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void should_pass_the_searched_movie_and_mark_the_release_as_from_a_search()
        {
            GivenSpecs(_pass);

            var decision = Subject.GetSearchDecision(_reports, _matrix, userInvokedSearch: true).Single();

            decision.RemoteMovie.ReleaseSource.Should().Be(ReleaseSourceType.UserInvokedSearch);
            Mocker.GetMock<IMovieParsingService>().Verify(p => p.Map(It.IsAny<ParsedMovieInfo>(), It.IsAny<string>(), It.IsAny<int>(), _matrix), Times.Once());
        }

        [Test]
        public void should_say_when_a_searched_release_cannot_be_parsed()
        {
            GivenSpecs(_pass);
            _reports[0].Title = "!!!";

            var decision = Subject.GetSearchDecision(_reports, _matrix).Single();

            decision.Rejections.Single().Reason.Should().Be(DownloadRejectionReason.UnableToParse);
        }

        [Test]
        public void should_ignore_an_unparseable_release_in_rss()
        {
            GivenSpecs(_pass);
            _reports[0].Title = "!!!";

            Subject.GetRssDecision(_reports).Should().BeEmpty();
        }

        [Test]
        public void should_mark_a_pushed_release()
        {
            GivenSpecs(_pass);

            Subject.GetRssDecision(_reports, pushedRelease: true).Single().RemoteMovie.ReleaseSource.Should().Be(ReleaseSourceType.ReleasePush);
        }
    }
}
