using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Reflection;
using NzbDrone.Core.Datastore;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.ReflectionTests
{
    public class ReflectionExtensionFixture : TestBase
    {
        [Test]
        public void should_get_properties_from_models()
        {
            var models = Assembly.Load("Sonarr.Core").ImplementationsOf<ModelBase>();

            foreach (var model in models)
            {
                model.GetSimpleProperties().Should().NotBeEmpty();
            }
        }

        [Test]
        public void should_prefer_embarrs_own_type_over_a_class_ported_from_prowlarr_with_the_same_name()
        {
            var assembly = Assembly.Load("Sonarr.Core");

            var type = assembly.FindTypeByName("TorznabSettings");

            type.Should().NotBeNull();
            type.Namespace.Should().Be("NzbDrone.Core.Indexers.Torznab");
            assembly.FindTypeByName("TorznabSettings").Should().BeSameAs(type);
            assembly.FindTypeByName("IndexerDefinitionUpdateCommand").Should().NotBeNull();
            assembly.FindTypeByName("DoesNotExist").Should().BeNull();
        }

        [Test]
        public void should_be_able_to_get_implementations()
        {
            var models = Assembly.Load("Sonarr.Core").ImplementationsOf<ModelBase>();

            models.Should().NotBeEmpty();
        }
    }
}
