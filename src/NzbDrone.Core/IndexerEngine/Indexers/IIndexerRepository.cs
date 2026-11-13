namespace NzbDrone.Core.IndexerEngine.Indexers
{
    /// <summary>
    /// Where an engine indexer saves settings it learns while it runs (a login token, an API key it was given), so they
    /// survive a restart. The app stores the indexer; this is the engine's view of that store.
    /// </summary>
    public interface IIndexerRepository
    {
        void UpdateSettings(IndexerDefinition model);
    }
}
