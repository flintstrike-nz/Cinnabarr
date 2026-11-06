using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.CustomFormats.Movies
{
    [TestFixture]
    public class MovieCustomFormatSpecificationsFixture : CoreTest
    {
        private static CustomFormatInput Input(Quality quality = null, string title = "The.Matrix.1999.Remastered.1080p.BluRay.x264-GRP", string group = "GRP", string edition = "Remastered", int year = 1999)
        {
            return new CustomFormatInput
            {
                MovieInfo = new ParsedMovieInfo
                {
                    MovieTitles = new List<string> { "The Matrix" },
                    SimpleReleaseTitle = title,
                    ReleaseGroup = group,
                    Edition = edition,
                    Year = year,
                    Quality = new QualityModel(quality ?? Quality.Bluray1080p)
                },
                Movie = new Movie { MovieMetadata = new MovieMetadata { Year = 1999, OriginalLanguage = Language.Japanese } },
                Languages = new List<Language> { Language.English }
            };
        }

        [Test]
        public void release_title_should_match_a_movies_release_title()
        {
            new ReleaseTitleSpecification { Value = "Remastered" }.IsSatisfiedBy(Input()).Should().BeTrue();
            new ReleaseTitleSpecification { Value = "Extended" }.IsSatisfiedBy(Input()).Should().BeFalse();
        }

        [Test]
        public void release_group_should_match_a_movies_group()
        {
            new ReleaseGroupSpecification { Value = "^GRP$" }.IsSatisfiedBy(Input()).Should().BeTrue();
            new ReleaseGroupSpecification { Value = "^OTHER$" }.IsSatisfiedBy(Input()).Should().BeFalse();
        }

        [Test]
        public void source_and_resolution_should_read_a_movies_quality()
        {
            new SourceSpecification { Value = (int)QualitySource.Bluray }.IsSatisfiedBy(Input()).Should().BeTrue();
            new SourceSpecification { Value = (int)QualitySource.Web }.IsSatisfiedBy(Input()).Should().BeFalse();
            new ResolutionSpecification { Value = 1080 }.IsSatisfiedBy(Input()).Should().BeTrue();
            new ResolutionSpecification { Value = 2160 }.IsSatisfiedBy(Input()).Should().BeFalse();
        }

        [Test]
        public void source_should_know_the_movie_only_sources()
        {
            new SourceSpecification { Value = (int)QualitySource.Cam }.IsSatisfiedBy(Input(Quality.Cam)).Should().BeTrue();
            new SourceSpecification { Value = (int)QualitySource.BlurayDisk }.IsSatisfiedBy(Input(Quality.BrDisk)).Should().BeTrue();
        }

        [Test]
        public void edition_should_match_the_parsed_edition()
        {
            new EditionSpecification { Value = "Remaster" }.IsSatisfiedBy(Input()).Should().BeTrue();
            new EditionSpecification { Value = "Director" }.IsSatisfiedBy(Input()).Should().BeFalse();
            new EditionSpecification { Value = "Remaster" }.IsSatisfiedBy(Input(edition: null)).Should().BeFalse();
        }

        [Test]
        public void edition_should_not_match_a_series_release()
        {
            new EditionSpecification { Value = ".*" }.IsSatisfiedBy(new CustomFormatInput()).Should().BeFalse();
        }

        [TestCase(1990, 2000, true)]
        [TestCase(1999, 1999, true)]
        [TestCase(2000, 2010, false)]
        public void year_should_be_within_the_range(int min, int max, bool expected)
        {
            new YearSpecification { Min = min, Max = max }.IsSatisfiedBy(Input()).Should().Be(expected);
        }

        [Test]
        public void year_should_fall_back_to_the_movies_year_when_the_release_has_none()
        {
            new YearSpecification { Min = 1999, Max = 1999 }.IsSatisfiedBy(Input(year: 0)).Should().BeTrue();
        }

        [TestCase(nameof(Quality.Regional), QualityModifier.Regional)]
        [TestCase(nameof(Quality.DvdScreener), QualityModifier.Screener)]
        [TestCase(nameof(Quality.RAWHD), QualityModifier.RawHd)]
        [TestCase(nameof(Quality.BrDisk), QualityModifier.BrDisk)]
        [TestCase(nameof(Quality.Bluray1080pRemux), QualityModifier.Remux)]
        [TestCase(nameof(Quality.DVDR), QualityModifier.Remux)]
        [TestCase(nameof(Quality.Bluray1080p), QualityModifier.None)]
        [TestCase(nameof(Quality.WEBDL1080p), QualityModifier.None)]
        public void quality_modifier_should_be_worked_out_from_the_quality(string qualityName, QualityModifier expected)
        {
            var quality = (Quality)typeof(Quality).GetProperty(qualityName).GetValue(null);

            QualityModifierSpecification.ModifierOf(quality).Should().Be(expected);
            new QualityModifierSpecification { Value = (int)expected }.IsSatisfiedBy(Input(quality)).Should().BeTrue();
        }

        [Test]
        public void original_language_should_be_the_movies_original_language()
        {
            var spec = new LanguageSpecification { Value = Language.Original.Id };

            var japanese = Input();
            japanese.Languages = new List<Language> { Language.Japanese };

            spec.IsSatisfiedBy(japanese).Should().BeTrue();
            spec.IsSatisfiedBy(Input()).Should().BeFalse();
        }

        [Test]
        public void should_work_out_the_formats_of_a_release_for_a_movie()
        {
            var format = new CustomFormat("Remastered", new EditionSpecification { Name = "Remaster", Value = "Remaster" }) { Id = 1 };

            Mocker.GetMock<ICustomFormatService>().Setup(s => s.All()).Returns(new List<CustomFormat> { format });

            var remote = new RemoteMovie
            {
                ParsedMovieInfo = Input().MovieInfo,
                Movie = Input().Movie,
                Release = new ReleaseInfo(),
                Languages = new List<Language> { Language.English }
            };

            Mocker.Resolve<CustomFormatCalculationService>().ParseCustomFormat(remote, 1000).Should().ContainSingle().Which.Name.Should().Be("Remastered");
        }

        [Test]
        public void should_work_out_the_formats_of_a_movie_file()
        {
            var format = new CustomFormat("Blu-ray", new SourceSpecification { Name = "Bluray", Value = (int)QualitySource.Bluray }) { Id = 2 };

            Mocker.GetMock<ICustomFormatService>().Setup(s => s.All()).Returns(new List<CustomFormat> { format });

            var file = new MovieFile
            {
                RelativePath = "The Matrix (1999).mkv",
                Quality = new QualityModel(Quality.Bluray1080p),
                Movie = Input().Movie,
                Languages = new List<Language> { Language.English }
            };

            Mocker.Resolve<CustomFormatCalculationService>().ParseCustomFormat(file).Should().ContainSingle().Which.Name.Should().Be("Blu-ray");
        }
    }
}
