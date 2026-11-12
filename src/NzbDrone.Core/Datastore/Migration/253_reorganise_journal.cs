using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // The library reorganise tool's journal (roadmap §4.12): every file it renames, so a run can be undone.
    [Migration(253)]
    public class reorganise_journal : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("ReorganiseJournal")
                  .WithColumn("RunId").AsString().NotNullable()
                  .WithColumn("MediaType").AsString().NotNullable()
                  .WithColumn("MediaId").AsInt32().NotNullable()
                  .WithColumn("FileId").AsInt32().NotNullable()
                  .WithColumn("Title").AsString().Nullable()
                  .WithColumn("FromPath").AsString().NotNullable()
                  .WithColumn("ToPath").AsString().NotNullable()
                  .WithColumn("Applied").AsDateTime().NotNullable()
                  .WithColumn("Undone").AsDateTime().Nullable();

            Create.Index().OnTable("ReorganiseJournal").OnColumn("RunId").Ascending();
        }
    }
}
