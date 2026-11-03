using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Queue;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V3.Queue
{
    [V3ApiController("queue")]
    public class QueueActionController : Controller
    {
        private readonly IPendingReleaseService _pendingReleaseService;
        private readonly IDownloadService _downloadService;
        private readonly IQueueService _queueService;
        private readonly IEngineReAddService _engineReAddService;

        public QueueActionController(IPendingReleaseService pendingReleaseService,
                                     IDownloadService downloadService,
                                     IQueueService queueService,
                                     IEngineReAddService engineReAddService)
        {
            _pendingReleaseService = pendingReleaseService;
            _downloadService = downloadService;
            _queueService = queueService;
            _engineReAddService = engineReAddService;
        }

        // Sends a download that is missing from the built-in engine back to it (roadmap §4.4).
        [HttpPost("readd/{id:int}")]
        public async Task<object> ReAdd([FromRoute] int id)
        {
            var queueItem = _queueService.Find(id);

            if (queueItem == null)
            {
                throw new NotFoundException();
            }

            try
            {
                await _engineReAddService.ReAdd(queueItem.DownloadId);
            }
            catch (EngineReAddException ex)
            {
                throw new BadRequestException(ex.Message);
            }

            return new { };
        }

        [HttpPost("grab/{id:int}")]
        public async Task<object> Grab([FromRoute] int id)
        {
            var pendingRelease = _pendingReleaseService.FindPendingQueueItem(id);

            if (pendingRelease == null)
            {
                throw new NotFoundException();
            }

            await _downloadService.DownloadReport(pendingRelease.RemoteEpisode, null);

            return new { };
        }

        [HttpPost("grab/bulk")]
        [Consumes("application/json")]
        public async Task<object> Grab([FromBody] QueueBulkResource resource)
        {
            foreach (var id in resource.Ids)
            {
                var pendingRelease = _pendingReleaseService.FindPendingQueueItem(id);

                if (pendingRelease == null)
                {
                    throw new NotFoundException();
                }

                await _downloadService.DownloadReport(pendingRelease.RemoteEpisode, null);
            }

            return new { };
        }
    }
}
