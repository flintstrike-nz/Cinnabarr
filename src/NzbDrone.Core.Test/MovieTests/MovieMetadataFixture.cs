using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class MovieMetadataFixture : DbTest<MovieMetadataRepository, MovieMetadata>
    {
        private static MovieMetadata Metadata(int tmdbId, string title = "The Matrix")
        {
            return new MovieMetadata
            {
                TmdbId = tmdbId,
                ImdbId = "tt0133093",
                Title = title,
                CleanTitle = title.CleanMovieTitle(),
                SortTitle = title.ToLowerInvariant(),
                OriginalTitle = title,
                CleanOriginalTitle = title.CleanMovieTitle(),
                OriginalLanguage = Language.English,
                Status = MovieStatusType.Released,
                Runtime = 136,
                Year = 1999,
                InCinemas = new DateTime(1999, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                Genres = new List<string> { "Action", "Science Fiction" },
                Keywords = new List<string> { "simulation" },
                Images = new List<MediaCover.MediaCover> { new(MediaCoverTypes.Poster, "https://image.example/poster.jpg") },
                Recommendations = new List<int> { 604, 605 },
                Ratings = new Ratings { Tmdb = new RatingChild { Votes = 25000, Value = 8.2m, Type = RatingType.User } },
                CollectionTmdbId = 2344,
                CollectionTitle = "The Matrix Collection"
            };
        }

        [Test]
        public void should_store_and_read_back_all_the_fields()
        {
            Subject.UpsertMany(new List<MovieMetadata> { Metadata(603) });

            var stored = Subject.FindByTmdbId(603);

            stored.Title.Should().Be("The Matrix");
            stored.CleanTitle.Should().Be("thematrix");
            stored.Genres.Should().Equal("Action", "Science Fiction");
            stored.Keywords.Should().Equal("simulation");
            stored.Recommendations.Should().Equal(604, 605);
            stored.Images.Should().ContainSingle().Which.CoverType.Should().Be(MediaCoverTypes.Poster);
            stored.Ratings.Tmdb.Votes.Should().Be(25000);
            stored.Ratings.Tmdb.Value.Should().Be(8.2m);
            stored.OriginalLanguage.Should().Be(Language.English);
            stored.Status.Should().Be(MovieStatusType.Released);
            stored.CollectionTmdbId.Should().Be(2344);
            stored.InCinemas.Should().Be(new DateTime(1999, 3, 31, 0, 0, 0, DateTimeKind.Utc));
        }

        [Test]
        public void should_find_by_imdb_id_and_by_collection()
        {
            Subject.UpsertMany(new List<MovieMetadata> { Metadata(603) });

            Subject.FindByImdbId("tt0133093").TmdbId.Should().Be(603);
            Subject.GetMoviesByCollectionTmdbId(2344).Should().ContainSingle();
            Subject.GetMoviesWithCollections().Should().ContainSingle();
        }

        [Test]
        public void should_update_an_existing_movie_by_tmdb_id_instead_of_adding_a_second()
        {
            Subject.UpsertMany(new List<MovieMetadata> { Metadata(603) });

            var changed = Metadata(603, "The Matrix (Remastered)");
            Subject.UpsertMany(new List<MovieMetadata> { changed });

            AllStoredModels.Should().ContainSingle().Which.Title.Should().Be("The Matrix (Remastered)");
        }

        [Test]
        public void should_report_no_change_when_nothing_differs()
        {
            Subject.UpsertMany(new List<MovieMetadata> { Metadata(603) });

            Subject.UpsertMany(new List<MovieMetadata> { Metadata(603) }).Should().BeFalse();
        }
    }

    [TestFixture]
    public class MovieTitleCleaningFixture
    {
        [TestCase("The Matrix", "thematrix")]
        [TestCase("Amélie", "amelie")]
        [TestCase("Das Boot: Director's Cut", "dasbootdirectorscut")]
        [TestCase("Über alles", "ueberalles")]
        [TestCase("2012", "2012")]
        public void should_clean_titles_for_comparison(string title, string expected)
        {
            title.CleanMovieTitle().Should().Be(expected);
        }

        [Test]
        public void alternative_title_should_clean_its_own_title()
        {
            new AlternativeTitle("The Matrix").CleanTitle.Should().Be("thematrix");
        }

        [Test]
        public void credit_defaults_should_be_empty_not_null()
        {
            new Credit().Images.Should().BeEmpty();
            new Credit().Type.Should().Be(CreditType.Cast);
        }

        [Test]
        public void normalizer_should_use_precomputed_titles_for_known_oddities()
        {
            MovieTitleNormalizer.Normalize("A to Z", 387354).Should().Be("a to z");
            MovieTitleNormalizer.Normalize("The Matrix", 603).Should().Be("matrix");
        }
    }
}
