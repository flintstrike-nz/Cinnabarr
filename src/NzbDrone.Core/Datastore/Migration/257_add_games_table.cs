using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(257)]
    public class add_games_table : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("Games")
                  .WithColumn("PlatformKey").AsString().NotNullable()
                  .WithColumn("Title").AsString().NotNullable()
                  .WithColumn("SortTitle").AsString().NotNullable()
                  .WithColumn("Region").AsString().Nullable()
                  .WithColumn("RootFolderPath").AsString().NotNullable()
                  .WithColumn("Path").AsString().NotNullable()
                  .WithColumn("Size").AsInt64().NotNullable()
                  .WithColumn("Added").AsDateTime().NotNullable();

            Create.Index().OnTable("Games").OnColumn("Path").Ascending();
        }
    }
}
