using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.IndexerEngine.Indexers.Definitions.Cardigann;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Engine;
using NzbDrone.Core.Localization;
using NzbDrone.Test.Common;
using Sonarr.Api.V5.Indexers;
using Sonarr.Http.ClientSchema;
using EngineDefinition = NzbDrone.Core.IndexerEngine.Indexers.IndexerDefinition;
using EngineSettings = NzbDrone.Core.IndexerEngine.Indexers.Definitions.Cardigann.CardigannSettings;
using Settings = NzbDrone.Core.Indexers.Cardigann.CardigannSettings;

namespace NzbDrone.Api.Test.IndexerTests
{
    [TestFixture]
    public class IndexerResourceMapperFixture : TestBase
    {
        private IndexerResourceMapper _mapper;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .Returns<string, Dictionary<string, object>>((s, d) => s);

            SchemaBuilder.Initialize(Mocker.Container);

            var template = new EngineDefinition
            {
                Name = "My Tracker",
                Settings = new EngineSettings { DefinitionFile = "mytracker" },
                ExtraFields = new List<SettingsField>
                {
                    new() { Name = "username", Label = "Username", Type = "text" },
                    new() { Name = "password", Label = "Password", Type = "password" },
                    new() { Name = "freeleech", Label = "Free leech only", Type = "checkbox", Default = "false" },
                    new() { Name = "note", Type = "info" }
                }
            };

            var provider = new Mock<IEngineIndexerProvider>();
            provider.Setup(p => p.FindTemplate("mytracker")).Returns(template);

            _mapper = new IndexerResourceMapper(provider.Object);
        }

        private static IndexerDefinition Saved() => new()
        {
            Id = 3,
            Name = "My Tracker",
            Implementation = "Cardigann",
            ConfigContract = "CardigannSettings",
            Settings = new Settings
            {
                DefinitionFile = "mytracker",
                ExtraFieldData = new Dictionary<string, object> { { "username", "me" }, { "password", "hunter2" } }
            }
        };

        [Test]
        public void should_add_the_fields_the_definition_asks_for_and_hide_passwords()
        {
            var resource = _mapper.ToResource(Saved());

            var extra = resource.Fields.Where(f => f.Name.StartsWith("extra_")).ToList();

            extra.Select(f => f.Name).Should().Equal("extra_username", "extra_password", "extra_freeleech");
            extra.Single(f => f.Name == "extra_username").Value.Should().Be("me");
            extra.Single(f => f.Name == "extra_password").Value.Should().Be("********");
            extra.Single(f => f.Name == "extra_freeleech").Type.Should().Be("checkbox");
        }

        [Test]
        public void should_keep_the_saved_password_when_the_form_sends_the_hidden_value_back()
        {
            var saved = Saved();
            var resource = _mapper.ToResource(saved);

            resource.Fields.Single(f => f.Name == "extra_username").Value = JsonDocument.Parse("\"someone\"").RootElement;

            var model = _mapper.ToModel(resource, saved);

            var data = ((Settings)model.Settings).ExtraFieldData;
            data["username"].Should().Be("someone");
            data["password"].Should().Be("hunter2");
        }

        [Test]
        public void should_save_a_new_password()
        {
            var saved = Saved();
            var resource = _mapper.ToResource(saved);

            resource.Fields.Single(f => f.Name == "extra_password").Value = JsonDocument.Parse("\"new-secret\"").RootElement;

            ((Settings)_mapper.ToModel(resource, saved).Settings).ExtraFieldData["password"].Should().Be("new-secret");
        }
    }
}
