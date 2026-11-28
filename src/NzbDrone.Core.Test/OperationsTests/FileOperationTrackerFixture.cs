using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Operations;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.OperationsTests
{
    [TestFixture]
    public class FileOperationTrackerFixture : CoreTest<FileOperationTracker>
    {
        private string _source;
        private string _destination;

        [SetUp]
        public void Setup()
        {
            _source = @"C:\Downloads\Show.S01E01.mkv".AsOsAgnostic();
            _destination = @"C:\TV\Show\Season 01\Show - S01E01.mkv".AsOsAgnostic();
        }

        [Test]
        public void should_list_operations_in_the_order_they_were_queued()
        {
            Subject.Queue(FileOperationKinds.Import, "Show S01E01", _source);
            Subject.Queue(FileOperationKinds.Move, "Show S01E01", _source);
            Subject.Queue(FileOperationKinds.Import, "Show S01E02", "two");

            var all = Subject.All();

            all.Select(o => o.Kind).Should().Equal(FileOperationKinds.Import, FileOperationKinds.Move, FileOperationKinds.Import);
            all.Select(o => o.RunIndex).Should().BeInAscendingOrder();
            all.Should().OnlyContain(o => o.Status == FileOperationStatus.Queued);
        }

        [Test]
        public void should_use_the_step_queued_for_a_file_when_it_is_moved()
        {
            var import = Subject.Queue(FileOperationKinds.Import, "Show S01E01", _source);
            var move = Subject.Queue(FileOperationKinds.Move, "Show S01E01", _source);

            var started = Subject.StartTransfer("Show S01E01", _source, _destination, TransferMode.Move);

            started.Id.Should().Be(move.Id);
            started.Id.Should().NotBe(import.Id);
            Subject.Get(move.Id).Status.Should().Be(FileOperationStatus.Running);
            Subject.Get(move.Id).Destination.Should().Be(_destination);

            Subject.Complete(started);

            Subject.Get(move.Id).Status.Should().Be(FileOperationStatus.Completed);
            Subject.Get(move.Id).Progress.Should().Be(100);
            Subject.All().Should().HaveCount(2);
        }

        [Test]
        public void should_call_a_move_within_a_folder_a_rename()
        {
            var renamed = Path.Combine(Path.GetDirectoryName(_source), "Show - S01E01.mkv");

            Subject.StartTransfer("Show S01E01", _source, renamed, TransferMode.Move).Kind.Should().Be(FileOperationKinds.Rename);
            Subject.StartTransfer("Show S01E01", "other", _destination, TransferMode.Copy).Kind.Should().Be(FileOperationKinds.Copy);
        }

        [Test]
        public void should_say_why_a_transfer_did_not_happen()
        {
            var operation = Subject.StartTransfer("Show S01E01", _source, _destination, TransferMode.Move);

            Subject.Fail(operation, new FileAlreadyExistsException("exists", _destination));

            Subject.Get(operation.Id).Status.Should().Be(FileOperationStatus.Skipped);
            Subject.Get(operation.Id).Message.Should().Contain("already a file");

            var other = Subject.StartTransfer("Show S01E02", "two", _destination, TransferMode.Move);
            Subject.Fail(other, new FileNotFoundException("gone", "two"));

            Subject.Get(other.Id).Status.Should().Be(FileOperationStatus.Failed);
            Subject.Get(other.Id).Message.Should().Contain("isn't there");
        }

        [Test]
        public void should_end_the_steps_still_queued_for_a_file()
        {
            var move = Subject.Queue(FileOperationKinds.Move, "Show S01E01", _source);

            Subject.SkipQueued(_source, "Not done: Not an upgrade");

            Subject.Get(move.Id).Status.Should().Be(FileOperationStatus.Skipped);
            Subject.Get(move.Id).Message.Should().Be("Not done: Not an upgrade");
        }

        [Test]
        public void should_not_change_a_finished_step()
        {
            var operation = Subject.Queue(FileOperationKinds.Import, "Show S01E01", _source);

            Subject.Complete(operation);
            Subject.Fail(operation, "later");

            Subject.Get(operation.Id).Status.Should().Be(FileOperationStatus.Completed);
        }

        [Test]
        public void should_clear_only_finished_steps()
        {
            var done = Subject.Queue(FileOperationKinds.Import, "one", "one");
            Subject.Queue(FileOperationKinds.Import, "two", "two");
            Subject.Complete(done);

            Subject.ClearFinished();

            Subject.All().Single().Title.Should().Be("two");
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<FileOperationsClearedEvent>()), Times.Once());
        }

        [Test]
        public void should_ignore_a_missing_operation()
        {
            Subject.Start(null);
            Subject.Complete(null);
            Subject.Fail(null, new IOException("x"));
        }
    }
}
