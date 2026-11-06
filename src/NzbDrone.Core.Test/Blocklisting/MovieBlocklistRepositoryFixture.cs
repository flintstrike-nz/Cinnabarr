using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.History;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Blocklisting
{
    [TestFixture]
    public class MovieBlocklistRepositoryFixture : DbTest<MovieBlocklistRepository, MovieBlocklist>
    {
        private MovieBlocklist Blocked(int movieId, string title, string hash = null) => new()
        {
            MovieId = movieId,
            Quality = new QualityModel(Quality.Bluray720p),
            Languages = new List<Language> { Language.English },
            SourceTitle = title,
            TorrentInfoHash = hash,
            Date = DateTime.UtcNow
        };

        [Test]
        public void should_round_trip_a_blocklist_entry()
        {
            Subject.Insert(Blocked(5, "The.Matrix.1999", "HASH"));

            var stored = Subject.All().ToList();

            stored.Should().ContainSingle();
            stored[0].MovieId.Should().Be(5);
            stored[0].Quality.Quality.Should().Be(Quality.Bluray720p);
            stored[0].Languages.Should().ContainSingle().Which.Should().Be(Language.English);
        }

        [Test]
        public void should_find_by_title_and_hash_for_the_same_movie_only()
        {
            Subject.Insert(Blocked(5, "The.Matrix.1999", "HASH"));
            Subject.Insert(Blocked(6, "The.Matrix.1999", "HASH"));

            Subject.BlocklistedByTitle(5, "The.Matrix").Should().ContainSingle().Which.MovieId.Should().Be(5);
            Subject.BlocklistedByTorrentInfoHash(5, "HASH").Should().ContainSingle().Which.MovieId.Should().Be(5);
            Subject.BlocklistedByTorrentInfoHash(7, "HASH").Should().BeEmpty();
        }

        [Test]
        public void should_list_a_movies_blocklist_newest_first()
        {
            var older = Blocked(5, "older");
            older.Date = DateTime.UtcNow.AddDays(-2);
            Subject.Insert(older);
            Subject.Insert(Blocked(5, "newer"));
            Subject.Insert(Blocked(6, "other movie"));

            Subject.GetByMovieId(5).Select(b => b.SourceTitle).Should().Equal("newer", "older");
        }

        [Test]
        public void should_delete_for_movies()
        {
            Subject.Insert(Blocked(5, "a"));
            Subject.Insert(Blocked(6, "b"));

            Subject.DeleteForMovies(new List<int> { 5 });

            Subject.All().Should().ContainSingle().Which.MovieId.Should().Be(6);
        }
    }

    [TestFixture]
    public class MovieHistoryRepositoryFixture : DbTest<MovieHistoryRepository, MovieHistory>
    {
        private MovieHistory Entry(int movieId, MovieHistoryEventType type, string downloadId, DateTime date)
        {
            var history = new MovieHistory
            {
                MovieId = movieId,
                EventType = type,
                DownloadId = downloadId,
                SourceTitle = "The.Matrix.1999",
                Date = date,
                Quality = new QualityModel(Quality.Bluray1080p),
                Languages = new List<Language> { Language.English }
            };

            history.Data["indexer"] = "Site";

            return history;
        }

        [Test]
        public void should_round_trip_data_and_find_the_latest_for_a_download()
        {
            Subject.Insert(Entry(5, MovieHistoryEventType.Grabbed, "HASH", DateTime.UtcNow.AddHours(-2)));
            Subject.Insert(Entry(5, MovieHistoryEventType.DownloadFailed, "HASH", DateTime.UtcNow));

            var latest = Subject.MostRecentForDownloadId("HASH");

            latest.EventType.Should().Be(MovieHistoryEventType.DownloadFailed);
            latest.Data["indexer"].Should().Be("Site");
            Subject.FindByDownloadId("HASH").Should().HaveCount(2);
        }

        [Test]
        public void should_filter_since_a_date_by_event_type()
        {
            Subject.Insert(Entry(5, MovieHistoryEventType.Grabbed, "A", DateTime.UtcNow.AddDays(-3)));
            Subject.Insert(Entry(5, MovieHistoryEventType.Grabbed, "B", DateTime.UtcNow.AddHours(-1)));
            Subject.Insert(Entry(5, MovieHistoryEventType.DownloadFailed, "C", DateTime.UtcNow));

            Subject.Since(DateTime.UtcNow.AddDays(-1), MovieHistoryEventType.Grabbed).Should().ContainSingle().Which.DownloadId.Should().Be("B");
            Subject.Since(DateTime.UtcNow.AddDays(-1), null).Should().HaveCount(2);
        }

        [Test]
        public void should_delete_for_movies_and_find_the_most_recent_for_a_movie()
        {
            Subject.Insert(Entry(5, MovieHistoryEventType.Grabbed, "A", DateTime.UtcNow.AddHours(-1)));
            Subject.Insert(Entry(6, MovieHistoryEventType.Grabbed, "B", DateTime.UtcNow));

            Subject.MostRecentForMovie(6).DownloadId.Should().Be("B");

            Subject.DeleteForMovies(new List<int> { 5 });

            Subject.All().Should().ContainSingle().Which.MovieId.Should().Be(6);
        }
    }
}
