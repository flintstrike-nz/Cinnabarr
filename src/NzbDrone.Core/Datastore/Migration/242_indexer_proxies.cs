using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Proxies (FlareSolverr, HTTP, SOCKS) used by the indexer engine's HTTP client (roadmap Phase 3d).
    [Migration(242)]
    public class indexer_proxies : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("IndexerProxies")
                  .WithColumn("Name").AsString().NotNullable().Unique()
                  .WithColumn("Settings").AsString().NotNullable()
                  .WithColumn("Implementation").AsString().NotNullable()
                  .WithColumn("ConfigContract").AsString().Nullable()
                  .WithColumn("Tags").AsString().Nullable();
        }
    }
}
