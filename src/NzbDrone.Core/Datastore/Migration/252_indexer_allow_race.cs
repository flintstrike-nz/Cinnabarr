using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Race mode (roadmap Phase 5): an indexer's releases may only race others when the indexer allows it.
    [Migration(252)]
    public class indexer_allow_race : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Indexers").AddColumn("AllowRace").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
