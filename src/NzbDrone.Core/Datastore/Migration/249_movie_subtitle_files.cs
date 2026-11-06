using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Subtitles that come with a movie (roadmap Phase 4e).
    [Migration(249)]
    public class movie_subtitle_files : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("MovieSubtitleFiles")
                  .WithColumn("MovieId").AsInt32().NotNullable()
                  .WithColumn("MovieFileId").AsInt32().Nullable()
                  .WithColumn("RelativePath").AsString().NotNullable()
                  .WithColumn("Extension").AsString().NotNullable()
                  .WithColumn("Added").AsDateTime().NotNullable()
                  .WithColumn("LastUpdated").AsDateTime().NotNullable()
                  .WithColumn("Language").AsInt32().NotNullable()
                  .WithColumn("Copy").AsInt32().NotNullable().WithDefaultValue(0)
                  .WithColumn("LanguageTags").AsString().Nullable()
                  .WithColumn("Title").AsString().Nullable();

            Create.Index().OnTable("MovieSubtitleFiles").OnColumn("MovieId").Ascending();
        }
    }
}
