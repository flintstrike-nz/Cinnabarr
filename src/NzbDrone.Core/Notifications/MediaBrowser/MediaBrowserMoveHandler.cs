using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Notifications.Emby
{
    /// <summary>
    /// When a series or movie is moved to another folder (by hand, or by the storage balancer), tells Emby / Jellyfin
    /// the old path is gone and the new one is there, so the library updates without a full scan.
    /// </summary>
    public class MediaBrowserMoveHandler : IHandle<SeriesMovedEvent>, IHandle<MovieMovedEvent>
    {
        private readonly INotificationFactory _notificationFactory;
        private readonly IMediaBrowserService _mediaBrowserService;
        private readonly Logger _logger;

        public MediaBrowserMoveHandler(INotificationFactory notificationFactory, IMediaBrowserService mediaBrowserService, Logger logger)
        {
            _notificationFactory = notificationFactory;
            _mediaBrowserService = mediaBrowserService;
            _logger = logger;
        }

        public void Handle(SeriesMovedEvent message)
        {
            Refresh(message.SourcePath, message.DestinationPath);
        }

        public void Handle(MovieMovedEvent message)
        {
            Refresh(message.SourcePath, message.DestinationPath);
        }

        private void Refresh(string oldPath, string newPath)
        {
            var targets = _notificationFactory.GetAvailableProviders()
                                              .Where(n => n.Definition.Settings is MediaBrowserSettings settings && settings.UpdateLibrary)
                                              .Select(n => (MediaBrowserSettings)n.Definition.Settings);

            foreach (var settings in targets)
            {
                try
                {
                    _mediaBrowserService.UpdatePath(settings, oldPath, "Deleted");
                    _mediaBrowserService.UpdatePath(settings, newPath, "Created");
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to tell Emby / Jellyfin that {0} moved to {1}", oldPath, newPath);
                }
            }
        }
    }
}
