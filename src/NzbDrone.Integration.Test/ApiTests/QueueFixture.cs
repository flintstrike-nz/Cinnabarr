using System.Threading;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Integration.Test.Client;
using Sonarr.Api.V3.Queue;
using Sonarr.Http;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class QueueFixture : IntegrationTest
    {
        private PagingResource<QueueResource> GetFirstPage()
        {
            var request = Queue.BuildRequest();
            request.AddParameter("includeUnknownSeriesItems", true);

            return Queue.Get<PagingResource<QueueResource>>(request);
        }

        private void RefreshQueue()
        {
            var command = Commands.Post(new SimpleCommandResource { Name = "RefreshMonitoredDownloads" });

            for (var i = 0; i < 30; i++)
            {
                var updatedCommand = Commands.Get(command.Id);

                if (updatedCommand.Status == CommandStatus.Completed)
                {
                    return;
                }

                Thread.Sleep(1000);
                i++;
            }
        }

        [Test]
        public void ensure_queue_is_empty_without_downloads()
        {
            RefreshQueue();

            var queue = GetFirstPage();

            queue.TotalRecords.Should().Be(0);
            queue.Records.Should().BeEmpty();
        }
    }
}
