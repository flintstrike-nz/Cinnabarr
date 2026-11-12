using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Version slots for series (roadmap Phase 5), as for movies: a series can hold several named versions, such as "4K"
    // and "HD". The series' own quality profile, monitored state and episode files are its default slot (slot id 0), so
    // only the extra slots are stored. A file of an extra slot is not an episode's own file (Episodes.EpisodeFileId); the
    // episodes it covers are recorded here instead.
    [Migration(254)]
    public class series_version_slots : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("SeriesVersionSlots")
                  .WithColumn("SeriesId").AsInt32().NotNullable()
                  .WithColumn("Label").AsString().NotNullable()
                  .WithColumn("QualityProfileId").AsInt32().NotNullable()
                  .WithColumn("Monitored").AsBoolean().NotNullable().WithDefaultValue(true)
                  .WithColumn("Added").AsDateTime().NotNullable();

            Create.Index().OnTable("SeriesVersionSlots").OnColumn("SeriesId").Ascending();

            Create.TableForModel("EpisodeVersionFiles")
                  .WithColumn("EpisodeId").AsInt32().NotNullable()
                  .WithColumn("VersionSlotId").AsInt32().NotNullable()
                  .WithColumn("EpisodeFileId").AsInt32().NotNullable();

            Create.Index().OnTable("EpisodeVersionFiles").OnColumn("EpisodeId").Ascending();
            Create.Index().OnTable("EpisodeVersionFiles").OnColumn("EpisodeFileId").Ascending();

            Alter.Table("EpisodeFiles").AddColumn("VersionSlotId").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }
}
