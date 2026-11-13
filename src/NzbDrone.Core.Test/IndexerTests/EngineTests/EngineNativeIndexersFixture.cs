using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.IndexerEngine.Indexers.Definitions.PassThePopcorn;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Cardigann;
using NzbDrone.Core.Indexers.Engine;
using NzbDrone.Core.Test.Framework;
using EngineIndexerDefinition = NzbDrone.Core.IndexerEngine.Indexers.IndexerDefinition;

namespace NzbDrone.Core.Test.IndexerTests.EngineTests
{
    [TestFixture]
    public class EngineNativeIndexersFixture : CoreTest<EngineNativeIndexers>
    {
        private PassThePopcorn _ptp;

        [SetUp]
        public void SetUp()
        {
            _ptp = Mocker.Resolve<PassThePopcorn>();

            // Only this one native can be created here; the others are left out of the list, as one that fails to create is.
            Mocker.GetMock<IServiceProvider>()
                  .Setup(s => s.GetService(typeof(PassThePopcorn)))
                  .Returns(_ptp);

            Mocker.GetMock<NzbDrone.Core.Localization.ILocalizationService>()
                  .Setup(l => l.GetLocalizedString(It.IsAny<string>()))
                  .Returns<string>(k => k);
        }

        [Test]
        public void should_list_an_engine_indexer_written_as_code_with_the_fields_its_settings_ask_for()
        {
            var template = Subject.Templates().Single();

            template.Name.Should().Be("PassThePopcorn");
            template.ExtraFields.Select(f => f.Name).Should().Contain(new[] { "APIUser", "APIKey", "FreeleechOnly", "GoldenPopcornOnly" });
            template.ExtraFields.Single(f => f.Name == "APIKey").Type.Should().Be("password");
            template.ExtraFields.Single(f => f.Name == "FreeleechOnly").Type.Should().Be("checkbox");
            ((NzbDrone.Core.IndexerEngine.Indexers.Definitions.Cardigann.CardigannSettings)template.Settings).DefinitionFile.Should().Be("native:PassThePopcorn");

            Subject.IsNative("native:PassThePopcorn").Should().BeTrue();
            Subject.IsNative("1337x").Should().BeFalse();
            Subject.FindTemplate("native:PassThePopcorn").Should().BeSameAs(template);
        }

        [Test]
        public void should_build_the_engine_settings_from_what_was_saved_whatever_the_case_of_the_keys()
        {
            var template = Subject.FindTemplate("native:PassThePopcorn");

            // The database keeps keys in camel case.
            var settings = new CardigannSettings
            {
                DefinitionFile = "native:PassThePopcorn",
                MinimumSeeders = 4,
                ExtraFieldData = new Dictionary<string, object> { ["apiUser"] = "me", ["apiKey"] = "secret", ["freeleechOnly"] = true }
            };

            var indexer = Subject.Create(new IndexerDefinition { Id = 3, Name = "PTP", Settings = settings }, settings, template);
            var engineSettings = (PassThePopcornSettings)indexer.Definition.Settings;

            engineSettings.APIUser.Should().Be("me");
            engineSettings.APIKey.Should().Be("secret");
            engineSettings.FreeleechOnly.Should().BeTrue();
            engineSettings.GoldenPopcornOnly.Should().BeFalse();
            engineSettings.BaseUrl.Should().Be(template.IndexerUrls.First());
            engineSettings.TorrentBaseSettings.AppMinimumSeeders.Should().Be(4);
            indexer.Definition.Id.Should().Be(3);
        }

        [Test]
        public void should_keep_settings_an_engine_indexer_learned_with_the_saved_indexer()
        {
            var stored = new IndexerDefinition { Id = 3, Settings = new CardigannSettings { DefinitionFile = "native:PassThePopcorn" } };

            Mocker.GetMock<IIndexerRepository>().Setup(r => r.Get(3)).Returns(stored);

            Subject.SaveSettings(new EngineIndexerDefinition { Id = 3, Settings = new PassThePopcornSettings { APIUser = "me", APIKey = "new-key" } });

            Mocker.GetMock<IIndexerRepository>().Verify(r => r.Update(stored), Times.Once());
            ((CardigannSettings)stored.Settings).ExtraFieldData["APIKey"].Should().Be("new-key");
            ((CardigannSettings)stored.Settings).ExtraFieldData["APIUser"].Should().Be("me");
        }

        [Test]
        public void should_ignore_settings_for_an_indexer_that_is_no_longer_saved()
        {
            Mocker.GetMock<IIndexerRepository>().Setup(r => r.Get(3)).Throws(new ModelNotFoundException(typeof(IndexerDefinition), 3));

            Subject.SaveSettings(new EngineIndexerDefinition { Id = 3, Settings = new PassThePopcornSettings() });

            Mocker.GetMock<IIndexerRepository>().Verify(r => r.Update(It.IsAny<IndexerDefinition>()), Times.Never());
        }
    }
}
