using System;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Download.Engine;

namespace NzbDrone.Core.Download.Clients.QBittorrent
{
    // API https://github.com/qbittorrent/qBittorrent/wiki/Web-API-Documentation

    public class QBittorrentProxyV2 : IQBittorrentProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly ICached<Dictionary<string, string>> _authCookieCache;

        public QBittorrentProxyV2(IHttpClient httpClient, ICacheManager cacheManager, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            _authCookieCache = cacheManager.GetCache<Dictionary<string, string>>(GetType(), "authCookies");
        }

        public bool IsApiSupported(QBittorrentSettings settings)
        {
            // We can do the api test without having to authenticate since v3.2.0-v4.0.4 will return 404 on the request.
            var request = BuildRequest(settings).Resource("/api/v2/app/webapiVersion");
            request.SuppressHttpError = true;

            try
            {
                var response = _httpClient.Execute(request.Build());

                // Version request will return 404 if it doesn't exist.
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return true;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new DownloadClientException("Failed to connect to qBittorrent. Check your settings and qBittorrent configuration.", new HttpException(response));
                }

                if (response.HasHttpError)
                {
                    throw new DownloadClientException("Failed to connect to qBittorrent, check your settings.", new HttpException(response));
                }

                return true;
            }
            catch (WebException ex)
            {
                if (ex.Status == WebExceptionStatus.TrustFailure)
                {
                    throw new DownloadClientUnavailableException("Unable to connect to qBittorrent, certificate validation failed.", ex);
                }

                throw new DownloadClientException("Failed to connect to qBittorrent, check your settings.", ex);
            }
        }

        public Version GetApiVersion(QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/app/webapiVersion");
            var response = Version.Parse(ProcessRequest(request, settings));

            return response;
        }

        public string GetVersion(QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/app/version");
            var response = ProcessRequest(request, settings).TrimStart('v');

            // eg "4.2alpha"
            return response;
        }

        public QBittorrentPreferences GetConfig(QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/app/preferences");
            var response = ProcessRequest<QBittorrentPreferences>(request, settings);

            return response;
        }

        public List<QBittorrentTorrent> GetTorrents(QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/info");
            if (settings.TvCategory.IsNotNullOrWhiteSpace())
            {
                request.AddQueryParam("category", settings.TvCategory);
            }

            var response = ProcessRequest<List<QBittorrentTorrent>>(request, settings);

            return response;
        }

        public bool IsTorrentLoaded(string hash, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/properties")
                                                .AddQueryParam("hash", hash);
            request.LogHttpError = false;

            try
            {
                ProcessRequest(request, settings);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public QBittorrentTorrentProperties GetTorrentProperties(string hash, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/properties")
                                                .AddQueryParam("hash", hash);
            var response = ProcessRequest<QBittorrentTorrentProperties>(request, settings);

            return response;
        }

        public List<QBittorrentTorrentFile> GetTorrentFiles(string hash, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/files")
                                                .AddQueryParam("hash", hash);
            var response = ProcessRequest<List<QBittorrentTorrentFile>>(request, settings);

            return response;
        }

        public void AddTorrentFromUrl(string torrentUrl, TorrentSeedConfiguration seedConfiguration, QBittorrentSettings settings, bool hold = false)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/add")
                                                .Post()
                                                .AddFormParameter("urls", torrentUrl);

            AddTorrentDownloadFormParameters(request, settings, hold, isMagnet: true);

            if (seedConfiguration != null)
            {
                AddTorrentSeedingFormParameters(request, seedConfiguration, settings);
            }

            var result = ProcessRequest(request, settings);

            // Note: Older qbit versions returned nothing, so we can't do != "Ok." here.
            if (result == "Fails.")
            {
                throw new DownloadClientException("Download client failed to add torrent by url");
            }
        }

        public void AddTorrentFromFile(string fileName, byte[] fileContent, TorrentSeedConfiguration seedConfiguration, QBittorrentSettings settings, bool hold = false)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/add")
                                                .Post()
                                                .AddFormUpload("torrents", fileName, fileContent);

            AddTorrentDownloadFormParameters(request, settings, hold, isMagnet: false);

            if (seedConfiguration != null)
            {
                AddTorrentSeedingFormParameters(request, seedConfiguration, settings);
            }

            var result = ProcessRequest(request, settings);

            // Note: Current qbit versions return nothing, so we can't do != "Ok." here.
            if (result == "Fails.")
            {
                throw new DownloadClientException("Download client failed to add torrent");
            }
        }

        public void AddManualTorrentFromUrl(string torrentUrl, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/add")
                                                .Post()
                                                .AddFormParameter("urls", torrentUrl);

            AddTorrentDownloadFormParameters(request, settings, true, isMagnet: true, $"{EngineTorrentTags.Held},{EngineTorrentTags.Manual}");

            if (ProcessRequest(request, settings) == "Fails.")
            {
                throw new DownloadClientException("Download client failed to add torrent by url");
            }
        }

        public void AddManualTorrentFromFile(string fileName, byte[] fileContent, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/add")
                                                .Post()
                                                .AddFormUpload("torrents", fileName, fileContent);

            AddTorrentDownloadFormParameters(request, settings, true, isMagnet: false, $"{EngineTorrentTags.Held},{EngineTorrentTags.Manual}");

            if (ProcessRequest(request, settings) == "Fails.")
            {
                throw new DownloadClientException("Download client failed to add torrent");
            }
        }

        public void RemoveTorrent(string hash, bool removeData, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/delete")
                                                .Post()
                                                .AddFormParameter("hashes", hash)

                                                // Required by the engine and by qBittorrent itself, whichever way it is set.
                                                .AddFormParameter("deleteFiles", removeData ? "true" : "false");

            ProcessRequest(request, settings);
        }

        public void SetTorrentLabel(string hash, string label, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/setCategory")
                                                .Post()
                                                .AddFormParameter("hashes", hash)
                                                .AddFormParameter("category", label);
            ProcessRequest(request, settings);
        }

        public void AddLabel(string label, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/createCategory")
                                                .Post()
                                                .AddFormParameter("category", label);
            ProcessRequest(request, settings);
        }

        public Dictionary<string, QBittorrentLabel> GetLabels(QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/categories");
            return Json.Deserialize<Dictionary<string, QBittorrentLabel>>(ProcessRequest(request, settings));
        }

        private void AddTorrentSeedingFormParameters(HttpRequestBuilder request, TorrentSeedConfiguration seedConfiguration, QBittorrentSettings settings, bool always = false)
        {
            var ratioLimit = seedConfiguration.Ratio.HasValue ? seedConfiguration.Ratio : -2;
            var seedingTimeLimit = seedConfiguration.SeedTime.HasValue ? (long)seedConfiguration.SeedTime.Value.TotalMinutes : -2;

            if (ratioLimit != -2 || always)
            {
                request.AddFormParameter("ratioLimit", ratioLimit);
            }

            if (seedingTimeLimit != -2 || always)
            {
                request.AddFormParameter(GetSeedingTimeLimitParameterName(settings), seedingTimeLimit);
            }
        }

        // qBittorrent 5.1 and the built-in engine require both (the global setting decides them); older servers ignore them.
        private static void AddShareLimitModeParameters(HttpRequestBuilder request)
        {
            request.AddFormParameter("shareLimitAction", "Default");
            request.AddFormParameter("shareLimitsMode", "Default");
        }

        private string GetSeedingTimeLimitParameterName(QBittorrentSettings settings)
        {
            if ((QBittorrentSeedTimeType)settings.SeedTimeType == QBittorrentSeedTimeType.Total)
            {
                return "seedingTimeLimit";
            }

            // inactiveSeedingTimeLimit was added in api v2.9.2 (qBittorrent 4.6.0)
            if (GetApiVersion(settings) >= new Version(2, 9, 2))
            {
                return "inactiveSeedingTimeLimit";
            }

            _logger.Warn("Inactive seed time requires qBittorrent 4.6.0 or later, sending seed time as total seeding time instead");

            return "seedingTimeLimit";
        }

        private void AddTorrentDownloadFormParameters(HttpRequestBuilder request, QBittorrentSettings settings, bool hold, bool isMagnet, string holdTags = EngineTorrentTags.Held)
        {
            if (settings.TvCategory.IsNotNullOrWhiteSpace())
            {
                request.AddFormParameter("category", settings.TvCategory);
            }

            if (hold)
            {
                // Built-in engine: nothing is downloaded until Cinnabarr has set the file priorities (roadmap §4.8).
                // The held tag marks the torrent for preparation. A magnet fetches its metadata, then stops; a
                // .torrent file already has it, so it starts stopped.
                request.AddFormParameter("tags", holdTags);

                if (isMagnet)
                {
                    request.AddFormParameter("stopCondition", "MetadataReceived");
                }
                else
                {
                    request.AddFormParameter("stopped", true);
                }
            }
            else if ((QBittorrentState)settings.InitialState is QBittorrentState.Start or QBittorrentState.Stop)
            {
                // Avoid extraneous API version check if initial state is ForceStart
                var stoppedParameterName = GetApiVersion(settings) >= new Version(2, 11, 0) ? "stopped" : "paused";

                // Note: ForceStart is handled by separate api call
                if ((QBittorrentState)settings.InitialState == QBittorrentState.Start)
                {
                    request.AddFormParameter(stoppedParameterName, false);
                }
                else if ((QBittorrentState)settings.InitialState == QBittorrentState.Stop)
                {
                    request.AddFormParameter(stoppedParameterName, true);
                }
            }

            if (settings.SequentialOrder)
            {
                request.AddFormParameter("sequentialDownload", true);
            }

            if (settings.FirstAndLast)
            {
                request.AddFormParameter("firstLastPiecePrio", true);
            }

            if ((QBittorrentContentLayout)settings.ContentLayout == QBittorrentContentLayout.Original)
            {
                request.AddFormParameter("contentLayout", "Original");
            }
            else if ((QBittorrentContentLayout)settings.ContentLayout == QBittorrentContentLayout.Subfolder)
            {
                request.AddFormParameter("contentLayout", "Subfolder");
            }
        }

        public void SetTorrentSeedingConfiguration(string hash, TorrentSeedConfiguration seedConfiguration, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/setShareLimits")
                                                .Post()
                                                .AddFormParameter("hashes", hash);

            AddTorrentSeedingFormParameters(request, seedConfiguration, settings, true);
            AddShareLimitModeParameters(request);

            try
            {
                ProcessRequest(request, settings);
            }
            catch (DownloadClientException ex)
            {
                // setShareLimits was added in api v2.0.1 so catch it case of the unlikely event that someone has api v2.0
                if (ex.InnerException is HttpException httpException && httpException.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    return;
                }

                throw;
            }
        }

        public void MoveTorrentToTopInQueue(string hash, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/topPrio")
                                                .Post()
                                                .AddFormParameter("hashes", hash);

            try
            {
                ProcessRequest(request, settings);
            }
            catch (DownloadClientException ex)
            {
                // qBittorrent rejects all Prio commands with 409: Conflict if Options -> BitTorrent -> Torrent Queueing is not enabled
                if (ex.InnerException is HttpException httpException && httpException.Response.StatusCode == HttpStatusCode.Conflict)
                {
                    return;
                }

                throw;
            }
        }

        public void SetForceStart(string hash, bool enabled, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/setForceStart")
                                                .Post()
                                                .AddFormParameter("hashes", hash)
                                                .AddFormParameter("value", enabled ? "true" : "false");
            ProcessRequest(request, settings);
        }

        public void AddTags(string hash, IEnumerable<string> tags, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/addTags")
                .Post()
                .AddFormParameter("hashes", hash)
                .AddFormParameter("tags", string.Join(",", tags));
            ProcessRequest(request, settings);
        }

        public void SetFilePriority(string hash, IEnumerable<int> fileIndexes, int priority, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/filePrio")
                .Post()
                .AddFormParameter("hash", hash)
                .AddFormParameter("id", string.Join("|", fileIndexes))
                .AddFormParameter("priority", priority);
            ProcessRequest(request, settings);
        }

        public void RenameFile(string hash, string oldPath, string newPath, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/renameFile")
                .Post()
                .AddFormParameter("hash", hash)
                .AddFormParameter("oldPath", oldPath)
                .AddFormParameter("newPath", newPath);
            ProcessRequest(request, settings);
        }

        public void SetShareLimitsWithoutTimeLimits(string hash, double ratioLimit, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/setShareLimits")
                .Post()
                .AddFormParameter("hashes", hash)
                .AddFormParameter("ratioLimit", ratioLimit)
                .AddFormParameter("seedingTimeLimit", -1);

            // inactiveSeedingTimeLimit was added in api v2.9.2 (qBittorrent 4.6.0)
            if (GetApiVersion(settings) >= new Version(2, 9, 2))
            {
                request.AddFormParameter("inactiveSeedingTimeLimit", -1);
            }

            AddShareLimitModeParameters(request);

            ProcessRequest(request, settings);
        }

        public void RecheckTorrent(string hash, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/recheck")
                .Post()
                .AddFormParameter("hashes", hash);
            ProcessRequest(request, settings);
        }

        public void SetLocation(string hash, string location, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/torrents/setLocation")
                .Post()
                .AddFormParameter("hashes", hash)
                .AddFormParameter("location", location);
            ProcessRequest(request, settings);
        }

        public void StartTorrent(string hash, QBittorrentSettings settings)
        {
            // qBittorrent 5 (API 2.11) renamed resume to start
            var action = GetApiVersion(settings) >= new Version(2, 11, 0) ? "start" : "resume";

            var request = BuildRequest(settings).Resource($"/api/v2/torrents/{action}")
                .Post()
                .AddFormParameter("hashes", hash);
            ProcessRequest(request, settings);
        }

        public void StopTorrents(string hashes, QBittorrentSettings settings)
        {
            // qBittorrent 5 (API 2.11) renamed pause to stop
            var action = GetApiVersion(settings) >= new Version(2, 11, 0) ? "stop" : "pause";

            var request = BuildRequest(settings).Resource($"/api/v2/torrents/{action}")
                .Post()
                .AddFormParameter("hashes", hashes);
            ProcessRequest(request, settings);
        }

        public void StartTorrents(string hashes, QBittorrentSettings settings)
        {
            StartTorrent(hashes, settings);
        }

        public QBittorrentTransferInfo GetTransferInfo(QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/transfer/info");

            return ProcessRequest<QBittorrentTransferInfo>(request, settings);
        }

        public bool GetAlternativeSpeedLimitsEnabled(QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/transfer/speedLimitsMode");

            return ProcessRequest(request, settings).Trim() == "1";
        }

        public void ToggleAlternativeSpeedLimits(QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/transfer/toggleSpeedLimitsMode").Post();
            ProcessRequest(request, settings);
        }

        public JObject GetPreferencesJson(QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/app/preferences");

            return JObject.Parse(ProcessRequest(request, settings));
        }

        public void SetPreferencesJson(JObject preferences, QBittorrentSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v2/app/setPreferences")
                                                .Post()
                                                .AddFormParameter("json", preferences.ToString(Newtonsoft.Json.Formatting.None));
            ProcessRequest(request, settings);
        }

        private static HttpRequestBuilder BuildRequest(QBittorrentSettings settings)
        {
            var requestBuilder = new HttpRequestBuilder(settings.UseSsl, settings.Host, settings.Port, settings.UrlBase)
            {
                LogResponseContent = true,
                StoreRequestCookie = false
            };

            if (settings.ApiKey.IsNotNullOrWhiteSpace())
            {
                requestBuilder.Headers["Authorization"] = $"Bearer {settings.ApiKey}";
            }
            else if (settings.Username.IsNotNullOrWhiteSpace() || settings.Password.IsNotNullOrWhiteSpace())
            {
                requestBuilder.NetworkCredential = new BasicNetworkCredential(settings.Username, settings.Password);
            }

            return requestBuilder;
        }

        private TResult ProcessRequest<TResult>(HttpRequestBuilder requestBuilder, QBittorrentSettings settings)
            where TResult : new()
        {
            var responseContent = ProcessRequest(requestBuilder, settings);

            return Json.Deserialize<TResult>(responseContent);
        }

        private string ProcessRequest(HttpRequestBuilder requestBuilder, QBittorrentSettings settings)
        {
            if (settings.ApiKey.IsNotNullOrWhiteSpace())
            {
                var requestWithApiKey = requestBuilder.Build();
                requestWithApiKey.LogResponseContent = true;

                try
                {
                    return _httpClient.Execute(requestWithApiKey).Content;
                }
                catch (HttpException ex)
                {
                    if (ex.Response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        _logger.Debug(ex, "qbitTorrent authentication failed.");

                        throw new DownloadClientAuthenticationException("Failed to authenticate with qBittorrent.", ex);
                    }

                    throw new DownloadClientException("Failed to connect to qBittorrent, check your settings.", ex);
                }
                catch (Exception ex)
                {
                    throw new DownloadClientException("Failed to connect to qBittorrent, please check your settings.", ex);
                }
            }

            AuthenticateClient(requestBuilder, settings);

            var request = requestBuilder.Build();
            request.LogResponseContent = true;
            request.SuppressHttpErrorStatusCodes = [HttpStatusCode.Forbidden];

            try
            {
                var response = _httpClient.Execute(request);

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    _logger.Debug("Authentication required, logging in.");

                    AuthenticateClient(requestBuilder, settings, true);

                    request = requestBuilder.Build();

                    response = _httpClient.Execute(request);
                }

                return response.Content;
            }
            catch (HttpException ex)
            {
                throw new DownloadClientException("Failed to connect to qBittorrent, check your settings.", ex);
            }
            catch (Exception ex)
            {
                throw new DownloadClientException("Failed to connect to qBittorrent, please check your settings.", ex);
            }
        }

        private void AuthenticateClient(HttpRequestBuilder requestBuilder, QBittorrentSettings settings, bool reauthenticate = false)
        {
            if (settings.Username.IsNullOrWhiteSpace() || settings.Password.IsNullOrWhiteSpace())
            {
                if (reauthenticate)
                {
                    throw new DownloadClientAuthenticationException("Failed to authenticate with qBittorrent.");
                }

                return;
            }

            var authKey = $"{requestBuilder.BaseUrl}:{settings.Username}:{settings.Password}";

            var cookies = _authCookieCache.Find(authKey);

            if (cookies == null || reauthenticate)
            {
                _authCookieCache.Remove(authKey);

                var authRequestBuilder = BuildRequest(settings);

                var authLoginRequest = authRequestBuilder.Resource("/api/v2/auth/login")
                    .Post()
                    .AddFormParameter("username", settings.Username ?? string.Empty)
                    .AddFormParameter("password", settings.Password ?? string.Empty)
                    .Build();

                HttpResponse response;
                try
                {
                    response = _httpClient.Execute(authLoginRequest);
                }
                catch (HttpException ex)
                {
                    _logger.Debug(ex, "qbitTorrent authentication failed.");

                    if (ex.Response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        throw new DownloadClientAuthenticationException("Failed to authenticate with qBittorrent.", ex);
                    }

                    throw new DownloadClientException("Failed to connect to qBittorrent, please check your settings.", ex);
                }
                catch (WebException ex)
                {
                    throw new DownloadClientUnavailableException("Failed to connect to qBittorrent, please check your settings.", ex);
                }

                if (response.Content.IsNotNullOrWhiteSpace() && response.Content != "Ok.")
                {
                    // returns "Fails." on bad login
                    _logger.Debug("qbitTorrent authentication failed.");
                    throw new DownloadClientAuthenticationException("Failed to authenticate with qBittorrent.");
                }

                _logger.Debug("qBittorrent authentication succeeded.");

                cookies = response.GetCookies();

                _authCookieCache.Set(authKey, cookies);
            }

            requestBuilder.SetCookies(cookies);
        }
    }
}
