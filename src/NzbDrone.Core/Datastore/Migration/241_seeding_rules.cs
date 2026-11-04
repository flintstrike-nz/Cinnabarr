using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Seeding goals per series and per episode (roadmap §4.10); a season's goal lives in the series' Seasons.
    [Migration(241)]
    public class seeding_rules : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Series").AddColumn("SeedingRule").AsString().Nullable();
            Alter.Table("Episodes").AddColumn("SeedingRule").AsString().Nullable();
        }
    }
}
