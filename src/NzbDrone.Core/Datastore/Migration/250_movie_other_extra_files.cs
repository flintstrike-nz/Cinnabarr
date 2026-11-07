using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // .nfo, artwork and other files that sit beside a movie file (roadmap Phase 4e).
    [Migration(250)]
    public class movie_other_extra_files : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("MovieOtherExtraFiles")
                  .WithColumn("MovieId").AsInt32().NotNullable()
                  .WithColumn("MovieFileId").AsInt32().Nullable()
                  .WithColumn("RelativePath").AsString().NotNullable()
                  .WithColumn("Extension").AsString().NotNullable()
                  .WithColumn("Added").AsDateTime().NotNullable()
                  .WithColumn("LastUpdated").AsDateTime().NotNullable();

            Create.Index().OnTable("MovieOtherExtraFiles").OnColumn("MovieId").Ascending();
        }
    }
}
