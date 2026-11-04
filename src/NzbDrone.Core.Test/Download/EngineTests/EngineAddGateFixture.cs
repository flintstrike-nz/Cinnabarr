using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download.Engine;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.EngineTests
{
    [TestFixture]
    public class EngineAddGateFixture : CoreTest<EngineAddGate>
    {
        [Test]
        public void should_allow_deleting_when_nothing_was_added()
        {
            using var gate = Subject.TryBeginDelete();

            gate.Should().NotBeNull();
        }

        [Test]
        public void should_not_allow_deleting_right_after_an_add()
        {
            Subject.BeginAdd().Dispose();

            Subject.TryBeginDelete().Should().BeNull();
        }

        [Test]
        public void should_release_the_gate_after_a_delete()
        {
            Subject.TryBeginDelete().Dispose();

            using var gate = Subject.TryBeginDelete();

            gate.Should().NotBeNull();
        }
    }
}
