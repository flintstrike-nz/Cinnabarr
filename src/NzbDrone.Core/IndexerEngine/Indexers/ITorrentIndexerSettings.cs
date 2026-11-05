namespace NzbDrone.Core.IndexerEngine.Indexers
{
    public interface ITorrentIndexerSettings : IIndexerSettings
    {
        IndexerTorrentBaseSettings TorrentBaseSettings { get; set; }
    }
}
