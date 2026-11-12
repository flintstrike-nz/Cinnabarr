using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Version slots (roadmap Phase 5): a movie can hold several named versions, such as "4K" and "HD". The movie's own
    // quality profile, monitored state and file are its default slot (slot id 0), so only the extra slots are stored here.
    [Migration(251)]
    public class version_slots : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("VersionSlots")
                  .WithColumn("MovieId").AsInt32().NotNullable()
                  .WithColumn("Label").AsString().NotNullable()
                  .WithColumn("QualityProfileId").AsInt32().NotNullable()
                  .WithColumn("Monitored").AsBoolean().NotNullable().WithDefaultValue(true)
                  .WithColumn("Added").AsDateTime().NotNullable();

            Create.Index().OnTable("VersionSlots").OnColumn("MovieId").Ascending();

            // The slot a file belongs to; 0 is the default slot, the one the movie itself points to.
            Alter.Table("MovieFiles").AddColumn("VersionSlotId").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }
}
