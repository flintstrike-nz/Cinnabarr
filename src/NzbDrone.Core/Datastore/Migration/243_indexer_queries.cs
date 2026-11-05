using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // What each indexer was asked and how it answered, for the indexer stats and the Activity page (roadmap Phase 3e).
    [Migration(243)]
    public class indexer_queries : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("IndexerQueries")
                  .WithColumn("IndexerId").AsInt32().NotNullable().Indexed()
                  .WithColumn("Date").AsDateTime().NotNullable().Indexed()
                  .WithColumn("Source").AsString().NotNullable()
                  .WithColumn("Query").AsString().Nullable()
                  .WithColumn("Successful").AsBoolean().NotNullable()
                  .WithColumn("ResultCount").AsInt32().NotNullable()
                  .WithColumn("ElapsedMs").AsInt32().NotNullable()
                  .WithColumn("Error").AsString().Nullable();
        }
    }
}
