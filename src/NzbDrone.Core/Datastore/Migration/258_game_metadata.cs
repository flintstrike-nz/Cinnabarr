using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(258)]
    public class add_game_metadata : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Games").AddColumn("Overview").AsString().Nullable();
            Alter.Table("Games").AddColumn("Year").AsInt32().Nullable();
            Alter.Table("Games").AddColumn("Genres").AsString().Nullable();
            Alter.Table("Games").AddColumn("Developer").AsString().Nullable();
            Alter.Table("Games").AddColumn("Rating").AsDouble().Nullable();
            Alter.Table("Games").AddColumn("CoverUrl").AsString().Nullable();
            Alter.Table("Games").AddColumn("IgdbId").AsInt32().Nullable();
            Alter.Table("Games").AddColumn("MetadataChecked").AsDateTime().Nullable();
        }
    }
}
