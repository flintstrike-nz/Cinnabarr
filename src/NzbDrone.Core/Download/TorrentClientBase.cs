using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MonoTorrent;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Download
{
    public abstract class TorrentClientBase<TSettings> : DownloadClientBase<TSettings>
        where TSettings : IProviderConfig, new()
    {
        protected readonly IHttpClient _httpClient;
        private readonly IBlocklistService _blocklistService;
        private readonly IMovieBlocklistService _movieBlocklistService;
        protected readonly ITorrentFileInfoReader _torrentFileInfoReader;

        protected TorrentClientBase(ITorrentFileInfoReader torrentFileInfoReader,
            IHttpClient httpClient,
            IConfigService configService,
            IDiskProvider diskProvider,
            ILocalizationService localizationService,
            IBlocklistService blocklistService,
            IMovieBlocklistService movieBlocklistService,
            Logger logger)
            : base(configService, diskProvider, logger, localizationService)
        {
            _httpClient = httpClient;
            _blocklistService = blocklistService;
            _movieBlocklistService = movieBlocklistService;
            _torrentFileInfoReader = torrentFileInfoReader;
        }

        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;

        public virtual bool PreferTorrentFile => false;

        protected abstract string AddFromMagnetLink(RemoteMedia remote, string hash, string magnetLink);
        protected abstract string AddFromTorrentFile(RemoteMedia remote, string hash, string filename, byte[] fileContent);

        public override async Task<string> Download(RemoteMedia remote, IIndexer indexer)
        {
            var torrentInfo = remote.Release as TorrentInfo;

            string magnetUrl = null;
            string torrentUrl = null;

            if (remote.Release.DownloadUrl.IsNotNullOrWhiteSpace() && remote.Release.DownloadUrl.StartsWith("magnet:"))
            {
                magnetUrl = remote.Release.DownloadUrl;
            }
            else
            {
                torrentUrl = remote.Release.DownloadUrl;
            }

            if (torrentInfo != null && !torrentInfo.MagnetUrl.IsNullOrWhiteSpace())
            {
                magnetUrl = torrentInfo.MagnetUrl;
            }

            if (PreferTorrentFile)
            {
                if (torrentUrl.IsNotNullOrWhiteSpace())
                {
                    try
                    {
                        return await DownloadFromWebUrl(remote, indexer, torrentUrl);
                    }
                    catch (ReleaseBlockedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (!magnetUrl.IsNullOrWhiteSpace())
                        {
                            throw;
                        }

                        _logger.Debug("Torrent download failed, trying magnet. ({0})", ex.Message);
                    }
                }

                if (magnetUrl.IsNotNullOrWhiteSpace())
                {
                    try
                    {
                        return DownloadFromMagnetUrl(remote, indexer, magnetUrl);
                    }
                    catch (NotSupportedException ex)
                    {
                        throw new ReleaseDownloadException(remote.Release, "Magnet not supported by download client. ({0})", ex.Message);
                    }
                }
            }
            else
            {
                if (magnetUrl.IsNotNullOrWhiteSpace())
                {
                    try
                    {
                        return DownloadFromMagnetUrl(remote, indexer, magnetUrl);
                    }
                    catch (NotSupportedException ex)
                    {
                        if (torrentUrl.IsNullOrWhiteSpace())
                        {
                            throw new ReleaseDownloadException(remote.Release, "Magnet not supported by download client. ({0})", ex.Message);
                        }

                        _logger.Debug("Magnet not supported by download client, trying torrent. ({0})", ex.Message);
                    }
                }

                if (torrentUrl.IsNotNullOrWhiteSpace())
                {
                    return await DownloadFromWebUrl(remote, indexer, torrentUrl);
                }
            }

            return null;
        }

        private async Task<string> DownloadFromWebUrl(RemoteMedia remote, IIndexer indexer, string torrentUrl)
        {
            byte[] torrentFile = null;

            try
            {
                if (indexer is IIndexerFileDownloader downloader)
                {
                    try
                    {
                        torrentFile = await downloader.DownloadTorrentFile(torrentUrl);
                    }
                    catch (Exception ex) when (ex is not HttpException and not WebException and not ReleaseDownloadException)
                    {
                        // Errors raised by the indexer engine (login failed, limits, bad response) become ordinary release failures.
                        _logger.Error(ex, "Downloading torrent file for release '{0}' failed ({1})", remote.Release.Title, torrentUrl);

                        throw new ReleaseDownloadException(remote.Release, "Downloading torrent failed", ex);
                    }

                    // The engine hands a magnet link back as text; follow it as a magnet rather than as a torrent file.
                    if (torrentFile.Length > 7 && Encoding.ASCII.GetString(torrentFile, 0, 7).Equals("magnet:", StringComparison.OrdinalIgnoreCase))
                    {
                        return DownloadFromMagnetUrl(remote, indexer, Encoding.UTF8.GetString(torrentFile).Trim());
                    }

                    _logger.Debug("Downloading torrent for release '{0}' finished ({1} bytes from {2})", remote.Release.Title, torrentFile.Length, torrentUrl);
                }
                else
                {
                    var request = indexer?.GetDownloadRequest(torrentUrl) ?? new HttpRequest(torrentUrl);
                    request.RateLimitKey = remote?.Release?.IndexerId.ToString();
                    request.Headers.Accept = "application/x-bittorrent";
                    request.AllowAutoRedirect = false;

                    var response = await RetryStrategy
                        .ExecuteAsync(static async (state, token) => await state._httpClient.GetAsync(state.request, token), (_httpClient, request))
                        .ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.MovedPermanently ||
                        response.StatusCode == HttpStatusCode.Found ||
                        response.StatusCode == HttpStatusCode.SeeOther)
                    {
                        var locationHeader = response.Headers.GetSingleValue("Location");

                        _logger.Trace("Torrent request is being redirected to: {0}", locationHeader);

                        if (locationHeader != null)
                        {
                            if (locationHeader.StartsWith("magnet:"))
                            {
                                return DownloadFromMagnetUrl(remote, indexer, locationHeader);
                            }

                            request.Url += new HttpUri(locationHeader);

                            return await DownloadFromWebUrl(remote, indexer, request.Url.ToString());
                        }

                        throw new WebException("Remote website tried to redirect without providing a location.");
                    }

                    torrentFile = response.ResponseData;

                    _logger.Debug("Downloading torrent for release '{0}' finished ({1} bytes from {2})", remote.Release.Title, torrentFile.Length, torrentUrl);
                }
            }
            catch (HttpException ex)
            {
                if (ex.Response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    _logger.Error(ex, "Downloading torrent file for release '{0}' failed since it no longer exists ({1})", remote.Release.Title, torrentUrl);
                    throw new ReleaseUnavailableException(remote.Release, "Downloading torrent failed", ex);
                }

                if ((int)ex.Response.StatusCode == 429)
                {
                    _logger.Error("API Grab Limit reached for {0}", torrentUrl);
                }
                else
                {
                    _logger.Error(ex, "Downloading torrent file for release '{0}' failed ({1})", remote.Release.Title, torrentUrl);
                }

                throw new ReleaseDownloadException(remote.Release, "Downloading torrent failed", ex);
            }
            catch (WebException ex)
            {
                _logger.Error(ex, "Downloading torrent file for release '{0}' failed ({1})", remote.Release.Title, torrentUrl);

                throw new ReleaseDownloadException(remote.Release, "Downloading torrent failed", ex);
            }

            var filename = string.Format("{0}.torrent", FileNameBuilder.CleanFileName(remote.Release.Title));
            var hash = _torrentFileInfoReader.GetHashFromTorrentFile(torrentFile);

            EnsureReleaseIsNotBlocklisted(remote, indexer, hash);
            EnsureTorrentDoesNotContainRejectedFiles(remote, indexer, torrentFile);

            var actualHash = AddFromTorrentFile(remote, hash, filename, torrentFile);

            if (actualHash.IsNotNullOrWhiteSpace() && hash != actualHash)
            {
                _logger.Debug(
                    "{0} did not return the expected InfoHash for '{1}', Sonarr could potentially lose track of the download in progress.",
                    Definition.Implementation,
                    remote.Release.DownloadUrl);
            }

            return actualHash;
        }

        private string DownloadFromMagnetUrl(RemoteMedia remote, IIndexer indexer, string magnetUrl)
        {
            string hash = null;
            string actualHash = null;

            try
            {
                hash = MagnetLink.Parse(magnetUrl).InfoHashes.V1OrV2.ToHex();
            }
            catch (FormatException ex)
            {
                throw new ReleaseDownloadException(remote.Release, "Failed to parse magnetlink for release '{0}': '{1}'", ex, remote.Release.Title, magnetUrl);
            }

            if (hash != null)
            {
                EnsureReleaseIsNotBlocklisted(remote, indexer, hash);

                actualHash = AddFromMagnetLink(remote, hash, magnetUrl);
            }

            if (actualHash.IsNotNullOrWhiteSpace() && hash != actualHash)
            {
                _logger.Debug(
                    "{0} did not return the expected InfoHash for '{1}', Sonarr could potentially lose track of the download in progress.",
                    Definition.Implementation,
                    remote.Release.DownloadUrl);
            }

            return actualHash;
        }

        private void EnsureTorrentDoesNotContainRejectedFiles(RemoteMedia remote, IIndexer indexer, byte[] torrentFile)
        {
            var indexerSettings = indexer?.Definition?.Settings as ITorrentIndexerSettings;

            if (indexerSettings?.RejectTorrentFilesWithBlockedExtensionsWhileGrabbing != true)
            {
                return;
            }

            var failDownloads = indexerSettings.FailDownloads?
                .Select(f => (FailDownloads)f)
                .ToHashSet();

            if (failDownloads == null || failDownloads.Count == 0)
            {
                return;
            }

            List<string> fileNames;

            try
            {
                fileNames = _torrentFileInfoReader.GetFileNamesFromTorrentFile(torrentFile);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to parse file list from torrent for '{0}', skipping file extension check", remote.Release.Title);
                return;
            }

            ValidateFileNames(remote, fileNames, failDownloads);
        }

        private void ValidateFileNames(RemoteMedia remote, List<string> fileNames, HashSet<FailDownloads> failDownloads)
        {
            var userRejectedExtensions = failDownloads.Contains(FailDownloads.UserDefinedExtensions)
                ? FileExtensions.ParseUserRejectedExtensions(_configService.UserRejectedExtensions)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var dangerousExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var executableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var userRejected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var fileName in fileNames)
            {
                var extension = Path.GetExtension(fileName);

                if (extension.IsNullOrWhiteSpace())
                {
                    continue;
                }

                if (failDownloads.Contains(FailDownloads.PotentiallyDangerous) &&
                    FileExtensions.DangerousExtensions.Contains(extension))
                {
                    dangerousExtensions.Add(extension);
                }
                else if (failDownloads.Contains(FailDownloads.Executables) &&
                    FileExtensions.ExecutableExtensions.Contains(extension))
                {
                    executableExtensions.Add(extension);
                }
                else if (userRejectedExtensions.Contains(extension))
                {
                    userRejected.Add(extension);
                }
            }

            var rejections = new List<string>();

            if (dangerousExtensions.Any())
            {
                rejections.Add($"Found potentially dangerous files with extensions: {string.Join(", ", dangerousExtensions)}");
            }

            if (executableExtensions.Any())
            {
                rejections.Add($"Found executables with extensions: {string.Join(", ", executableExtensions)}");
            }

            if (userRejected.Any())
            {
                rejections.Add($"Found files with user defined rejected extensions: {string.Join(", ", userRejected)}");
            }

            if (rejections.Count == 0)
            {
                return;
            }

            var rejection = $"Caution:{Environment.NewLine}{string.Join(Environment.NewLine, rejections)}";

            _logger.Warn("Torrent for '{0}' rejected: {1}. Blocklisting release.", remote.Release.Title, rejection);
            switch (remote)
            {
                case RemoteEpisode remoteEpisode:
                    _blocklistService.Block(remoteEpisode, rejection, "TorrentFileValidation");
                    break;
                case RemoteMovie remoteMovie:
                    _movieBlocklistService.Block(remoteMovie, rejection);
                    break;
            }

            throw new ReleaseBlockedException(remote.Release, rejection);
        }

        private bool IsHashBlocklisted(RemoteMedia remote, string hash)
        {
            return remote switch
            {
                RemoteEpisode remoteEpisode => _blocklistService.BlocklistedTorrentHash(remoteEpisode.Series.Id, hash),
                RemoteMovie remoteMovie => _movieBlocklistService.BlocklistedTorrentHash(remoteMovie.Movie.Id, hash),
                _ => false
            };
        }

        private void EnsureReleaseIsNotBlocklisted(RemoteMedia remote, IIndexer indexer, string hash)
        {
            var indexerSettings = indexer?.Definition?.Settings as ITorrentIndexerSettings;
            var torrentInfo = remote.Release as TorrentInfo;
            var torrentInfoHash = torrentInfo?.InfoHash;

            // If the release didn't come from an interactive search,
            // the hash wasn't known during processing and the
            // indexer is configured to reject blocklisted releases
            // during grab check if it's already been blocklisted.

            if (torrentInfo != null && torrentInfoHash.IsNullOrWhiteSpace())
            {
                // If the hash isn't known from parsing we set it here so it can be used for blocklisting.
                torrentInfo.InfoHash = hash;

                if (remote.ReleaseSource != ReleaseSourceType.InteractiveSearch &&
                    indexerSettings?.RejectBlocklistedTorrentHashesWhileGrabbing == true &&
                    IsHashBlocklisted(remote, hash))
                {
                    throw new ReleaseBlockedException(remote.Release, "Release previously added to blocklist");
                }
            }
        }
    }
}
