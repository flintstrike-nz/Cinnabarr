using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

        /// <summary>Every indexer Embarr can add from a definition file.</summary>
        IReadOnlyList<EngineDefinition> Templates();

        EngineDefinition FindTemplate(string definitionFile);
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

            var template = FindTemplate(settings.DefinitionFile)
                           ?? throw new NotSupportedException($"Indexer definition '{settings.DefinitionFile}' is not available; update the indexer definitions");

            var indexer = _services.GetRequiredService<EngineCardigann>();

            indexer.Definition = Build(definition, settings, template);

            return indexer;
        }

        public IReadOnlyList<EngineDefinition> Templates()
        {
            return LoadedTemplates().Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public EngineDefinition FindTemplate(string definitionFile)
        {
            return definitionFile != null && LoadedTemplates().TryGetValue(definitionFile, out var template) ? template : null;
        }

        private Dictionary<string, EngineDefinition> LoadedTemplates()
        {
            var templates = _templates.Get("all", LoadTemplates, TimeSpan.FromMinutes(10));

            // No definitions yet (the first download hasn't happened): don't remember the empty list.
            if (templates.Count == 0)
            {
                _templates.Remove("all");
            }

            return templates;
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
                    ExtraFieldData = NormalizeExtraFields(settings.ExtraFieldData),
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

        // Saved settings come back from the database with JsonElement values; the engine expects plain strings, bools and numbers.
        public static Dictionary<string, object> NormalizeExtraFields(Dictionary<string, object> data)
        {
            var result = new Dictionary<string, object>();

            foreach (var (key, value) in data ?? new Dictionary<string, object>())
            {
                result[key] = value is JsonElement element ? FromJson(element) : value;
            }

            return result;
        }

        private static object FromJson(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => element.TryGetInt64(out var whole) ? whole : element.GetDouble(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => element.ToString()
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
