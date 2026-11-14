using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(256)]
    public class doctor_flags : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("DoctorFlags")
                  .WithColumn("MediaType").AsInt32().NotNullable()
                  .WithColumn("ItemId").AsInt32().NotNullable()
                  .WithColumn("FileId").AsInt32().NotNullable()
                  .WithColumn("KeptFileId").AsInt32().NotNullable()
                  .WithColumn("Reason").AsString().Nullable()
                  .WithColumn("State").AsInt32().NotNullable()
                  .WithColumn("FlaggedAt").AsDateTime().NotNullable();

            Create.Index().OnTable("DoctorFlags").OnColumn("MediaType").Ascending().OnColumn("ItemId").Ascending();
        }
    }
}
