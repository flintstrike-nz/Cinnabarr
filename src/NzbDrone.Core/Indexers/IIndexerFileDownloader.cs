using System.Threading.Tasks;

namespace NzbDrone.Core.Indexers
{
    /// <summary>
    /// Implemented by indexers that must fetch the torrent file themselves because the download link
    /// needs a login session or cookies that a plain HTTP request would not have.
    /// </summary>
    public interface IIndexerFileDownloader
    {
        Task<byte[]> DownloadTorrentFile(string link);
    }
}
