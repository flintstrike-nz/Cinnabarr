using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Indexers.Cardigann;
using EngineCardigann = NzbDrone.Core.IndexerEngine.Indexers.Definitions.Cardigann.Cardigann;
using EngineCardigannSettings = NzbDrone.Core.IndexerEngine.Indexers.Definitions.Cardigann.CardigannSettings;
using EngineDefinition = NzbDrone.Core.IndexerEngine.Indexers.IndexerDefinition;
using EngineIndexer = NzbDrone.Core.IndexerEngine.Indexers.IIndexer;

namespace NzbDrone.Core.Indexers.Engine
{
    public interface IEngineIndexerProvider
    {
        /// <summary>The engine indexer that runs a saved Embarr indexer.</summary>
        EngineIndexer Get(IndexerDefinition definition);
    }

    public class EngineIndexerProvider : IEngineIndexerProvider
    {
        private readonly IServiceProvider _services;
        private readonly ICached<Dictionary<string, EngineDefinition>> _templates;

        public EngineIndexerProvider(IServiceProvider services, ICacheManager cacheManager)
        {
            _services = services;
            _templates = cacheManager.GetCache<Dictionary<string, EngineDefinition>>(GetType(), "templates");
        }

        public EngineIndexer Get(IndexerDefinition definition)
        {
            if (definition.Settings is not CardigannSettings settings)
            {
                throw new NotSupportedException($"Indexer {definition.Name} has no engine equivalent");
            }

            var templates = _templates.Get("all", LoadTemplates, TimeSpan.FromMinutes(10));

            if (!templates.TryGetValue(settings.DefinitionFile, out var template))
            {
                throw new NotSupportedException($"Indexer definition '{settings.DefinitionFile}' is not available; update the indexer definitions");
            }

            var indexer = _services.GetRequiredService<EngineCardigann>();

            indexer.Definition = Build(definition, settings, template);

            return indexer;
        }

        public static EngineDefinition Build(IndexerDefinition definition, CardigannSettings settings, EngineDefinition template)
        {
            return new EngineDefinition
            {
                Id = definition.Id,
                Name = definition.Name,
                Enable = definition.Enable,
                Implementation = nameof(EngineCardigann),
                ConfigContract = nameof(EngineCardigannSettings),
                Tags = definition.Tags,
                Priority = definition.Priority,
                Settings = new EngineCardigannSettings
                {
                    DefinitionFile = settings.DefinitionFile,
                    BaseUrl = settings.BaseUrl,
                    ExtraFieldData = settings.ExtraFieldData ?? new Dictionary<string, object>(),
                    TorrentBaseSettings =
                    {
                        AppMinimumSeeders = settings.MinimumSeeders
                    }
                },
                IndexerUrls = template.IndexerUrls,
                LegacyUrls = template.LegacyUrls,
                Description = template.Description,
                Language = template.Language,
                Protocol = template.Protocol,
                Privacy = template.Privacy,
                SupportsRss = template.SupportsRss,
                SupportsSearch = template.SupportsSearch,
                SupportsRedirect = template.SupportsRedirect,
                SupportsPagination = template.SupportsPagination,
                Capabilities = template.Capabilities,
                ExtraFields = template.ExtraFields
            };
        }

        private Dictionary<string, EngineDefinition> LoadTemplates()
        {
            var cardigann = _services.GetRequiredService<EngineCardigann>();

            return cardigann.DefaultDefinitions
                .OfType<EngineDefinition>()
                .Where(d => d.Settings is EngineCardigannSettings)
                .GroupBy(d => ((EngineCardigannSettings)d.Settings).DefinitionFile, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }
    }
}
