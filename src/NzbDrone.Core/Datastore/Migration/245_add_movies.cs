using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Movies and their files (roadmap Phase 4b). A movie points at its TMDb information (MovieMetadata) and at its one file.
    [Migration(245)]
    public class add_movies : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("Movies")
                .WithColumn("MovieMetadataId").AsInt32().Unique()
                .WithColumn("Path").AsString().NotNullable().Indexed()
                .WithColumn("Monitored").AsBoolean()
                .WithColumn("MinimumAvailability").AsInt32()
                .WithColumn("QualityProfileId").AsInt32()
                .WithColumn("Added").AsDateTime().Nullable()
                .WithColumn("Tags").AsString().Nullable()
                .WithColumn("AddOptions").AsString().Nullable()
                .WithColumn("LastSearchTime").AsDateTime().Nullable()
                .WithColumn("MovieFileId").AsInt32().WithDefaultValue(0);

            Create.TableForModel("MovieFiles")
                .WithColumn("MovieId").AsInt32().Indexed()
                .WithColumn("RelativePath").AsString().Nullable()
                .WithColumn("Size").AsInt64()
                .WithColumn("DateAdded").AsDateTime()
                .WithColumn("OriginalFilePath").AsString().Nullable()
                .WithColumn("SceneName").AsString().Nullable()
                .WithColumn("ReleaseGroup").AsString().Nullable()
                .WithColumn("Quality").AsString()
                .WithColumn("IndexerFlags").AsInt32().WithDefaultValue(0)
                .WithColumn("MediaInfo").AsString().Nullable()
                .WithColumn("Edition").AsString().Nullable()
                .WithColumn("Languages").AsString().Nullable();
        }
    }
}
