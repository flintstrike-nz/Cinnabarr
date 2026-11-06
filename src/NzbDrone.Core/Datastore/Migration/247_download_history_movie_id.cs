using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // A download can now be for a movie as well as a series (roadmap Phase 4d).
    [Migration(247)]
    public class download_history_movie_id : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("DownloadHistory").AddColumn("MovieId").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }
}
