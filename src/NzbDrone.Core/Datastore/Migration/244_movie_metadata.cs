using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Movie information from TMDb (roadmap Phase 4a). The same shape Radarr ended up with, created fresh because
    // Cinnabarr has no movie data to carry over.
    [Migration(244)]
    public class movie_metadata : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("MovieMetadata")
                .WithColumn("TmdbId").AsInt32().Unique()
                .WithColumn("ImdbId").AsString().Nullable()
                .WithColumn("Images").AsString()
                .WithColumn("Genres").AsString().Nullable()
                .WithColumn("Keywords").AsString().Nullable()
                .WithColumn("Title").AsString()
                .WithColumn("SortTitle").AsString().Nullable()
                .WithColumn("CleanTitle").AsString().Nullable().Indexed()
                .WithColumn("OriginalTitle").AsString().Nullable()
                .WithColumn("CleanOriginalTitle").AsString().Nullable().Indexed()
                .WithColumn("OriginalLanguage").AsInt32()
                .WithColumn("Status").AsInt32()
                .WithColumn("LastInfoSync").AsDateTime().Nullable()
                .WithColumn("Runtime").AsInt32()
                .WithColumn("InCinemas").AsDateTime().Nullable()
                .WithColumn("PhysicalRelease").AsDateTime().Nullable()
                .WithColumn("DigitalRelease").AsDateTime().Nullable()
                .WithColumn("Year").AsInt32().Nullable()
                .WithColumn("SecondaryYear").AsInt32().Nullable()
                .WithColumn("Ratings").AsString().Nullable()
                .WithColumn("Recommendations").AsString()
                .WithColumn("Certification").AsString().Nullable()
                .WithColumn("YouTubeTrailerId").AsString().Nullable()
                .WithColumn("CollectionTmdbId").AsInt32().Nullable().Indexed()
                .WithColumn("CollectionTitle").AsString().Nullable()
                .WithColumn("Studio").AsString().Nullable()
                .WithColumn("Overview").AsString().Nullable()
                .WithColumn("Website").AsString().Nullable()
                .WithColumn("Popularity").AsFloat().Nullable();

            Create.TableForModel("AlternativeTitles")
                .WithColumn("MovieMetadataId").AsInt32().Indexed()
                .WithColumn("SourceType").AsInt32()
                .WithColumn("Title").AsString()
                .WithColumn("CleanTitle").AsString().Indexed();

            Create.TableForModel("MovieTranslations")
                .WithColumn("MovieMetadataId").AsInt32().Indexed()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("CleanTitle").AsString().Nullable()
                .WithColumn("Overview").AsString().Nullable()
                .WithColumn("Language").AsInt32();

            Create.TableForModel("Credits")
                .WithColumn("MovieMetadataId").AsInt32().Indexed()
                .WithColumn("Name").AsString()
                .WithColumn("CreditTmdbId").AsString().Nullable()
                .WithColumn("PersonTmdbId").AsInt32()
                .WithColumn("Images").AsString()
                .WithColumn("Department").AsString().Nullable()
                .WithColumn("Job").AsString().Nullable()
                .WithColumn("Character").AsString().Nullable()
                .WithColumn("Order").AsInt32()
                .WithColumn("Type").AsInt32();
        }
    }
}
