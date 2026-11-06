using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // What was grabbed or blocked for a movie (roadmap Phase 4d), kept apart from the series tables.
    [Migration(246)]
    public class movie_history_blocklist : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("MovieHistory")
                .WithColumn("MovieId").AsInt32().NotNullable().Indexed()
                .WithColumn("SourceTitle").AsString()
                .WithColumn("Date").AsDateTime().Indexed()
                .WithColumn("Quality").AsString()
                .WithColumn("Data").AsString()
                .WithColumn("EventType").AsInt32().Nullable()
                .WithColumn("DownloadId").AsString().Nullable().Indexed()
                .WithColumn("Languages").AsString().Nullable();

            Create.TableForModel("MovieBlocklist")
                .WithColumn("MovieId").AsInt32().NotNullable().Indexed()
                .WithColumn("SourceTitle").AsString()
                .WithColumn("Quality").AsString()
                .WithColumn("Date").AsDateTime()
                .WithColumn("PublishedDate").AsDateTime().Nullable()
                .WithColumn("Size").AsInt64().Nullable()
                .WithColumn("Protocol").AsInt32().Nullable()
                .WithColumn("Indexer").AsString().Nullable()
                .WithColumn("IndexerFlags").AsInt32().WithDefaultValue(0)
                .WithColumn("Message").AsString().Nullable()
                .WithColumn("TorrentInfoHash").AsString().Nullable()
                .WithColumn("Languages").AsString().Nullable();
        }
    }
}
