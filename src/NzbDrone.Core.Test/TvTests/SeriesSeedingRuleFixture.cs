using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class SeriesSeedingRuleFixture : CoreTest
    {
        [Test]
        public void should_keep_season_seeding_rules_when_the_seasons_are_replaced_by_an_edit()
        {
            var rule = new EngineSeedingRule { Mode = EngineSeedingMode.Time, Minutes = 60 };
            var stored = new Series { Seasons = new List<Season> { new Season { SeasonNumber = 1, SeedingRule = rule } } };
            var edited = new Series { Seasons = new List<Season> { new Season { SeasonNumber = 1, Monitored = true } } };

            stored.ApplyChanges(edited);

            stored.Seasons[0].Monitored.Should().BeTrue();
            stored.Seasons[0].SeedingRule.Should().Be(rule);
        }
    }
}
