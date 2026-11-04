using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration;

[TestFixture]
public class seeding_rulesFixture : MigrationTest<seeding_rules>
{
    [Test]
    public void should_add_seeding_rule_columns_to_series_and_episodes()
    {
        var db = WithDapperMigrationTestDb();

        db.Execute(
            @"INSERT INTO ""Series"" (""TvdbId"", ""TmdbId"", ""Title"", ""CleanTitle"", ""TitleSlug"", ""Status"", ""Images"", ""Path"", ""Monitored"",
                                      ""SeasonFolder"", ""Runtime"", ""SeriesType"", ""UseSceneNumbering"", ""TvRageId"", ""TvMazeId"",
                                      ""OriginalLanguage"", ""MonitorNewItems"", ""MalIds"", ""AniListIds"", ""SeedingRule"")
              VALUES (1, 0, 'Title', 'title', 'title', 1, '[]', '/series/1', true, true, 0, 0, false, 0, 0, 1, 0, '[]', '[]', '{""mode"":1,""minutes"":60,""ratio"":0}')");

        db.QuerySingle<string>("SELECT \"SeedingRule\" FROM \"Series\"").Should().Contain("\"minutes\":60");
        db.QuerySingle<int>("SELECT COUNT(*) FROM \"Episodes\" WHERE \"SeedingRule\" IS NULL").Should().Be(0);
    }
}
