using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Queue;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Queue
{
    [V5ApiController("queue")]
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
        public async Task<Results<NoContent, NotFound>> ReAdd([FromRoute] int id)
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

            return TypedResults.NoContent();
        }

        [HttpPost("grab/{id:int}")]
        public async Task<Results<NoContent, NotFound>> Grab([FromRoute] int id)
        {
            var pendingRelease = _pendingReleaseService.FindPendingQueueItem(id);

            if (pendingRelease == null)
            {
                throw new NotFoundException();
            }

            await _downloadService.DownloadReport(pendingRelease.RemoteEpisode, null);

            return TypedResults.NoContent();
        }

        [HttpPost("grab/bulk")]
        [Consumes("application/json")]
        public async Task<Results<NoContent, NotFound>> Grab([FromBody] QueueBulkResource resource)
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

            return TypedResults.NoContent();
        }
    }
}
