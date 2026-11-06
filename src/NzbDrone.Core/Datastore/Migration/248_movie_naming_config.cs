using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // How movie files and folders are named (roadmap Phase 4e).
    [Migration(248)]
    public class movie_naming_config : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("NamingConfig").AddColumn("RenameMovies").AsBoolean().WithDefaultValue(false);
            Alter.Table("NamingConfig").AddColumn("StandardMovieFormat").AsString().WithDefaultValue("{Movie Title} ({Release Year}) {Quality Full}");
            Alter.Table("NamingConfig").AddColumn("MovieFolderFormat").AsString().WithDefaultValue("{Movie Title} ({Release Year})");
        }
    }
}
