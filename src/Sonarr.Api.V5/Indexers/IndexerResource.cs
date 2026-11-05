using System.Text.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Cardigann;
using NzbDrone.Core.Indexers.Engine;
using Sonarr.Api.V5.Provider;
using Sonarr.Http.ClientSchema;

namespace Sonarr.Api.V5.Indexers;

public class IndexerResource : ProviderResource<IndexerResource>
{
    public bool EnableRss { get; set; }
    public bool EnableAutomaticSearch { get; set; }
    public bool EnableInteractiveSearch { get; set; }
    public bool SupportsRss { get; set; }
    public bool SupportsSearch { get; set; }
    public DownloadProtocol Protocol { get; set; }
    public int Priority { get; set; }
    public int SeasonSearchMaximumSingleEpisodeAge { get; set; }
    public int DownloadClientId { get; set; }
}

public class IndexerResourceMapper : ProviderResourceMapper<IndexerResource, IndexerDefinition>
{
    private const string ExtraPrefix = "extra_";
    private const string PrivateValue = "********";

    private readonly IEngineIndexerProvider? _engineProvider;

    public IndexerResourceMapper(IEngineIndexerProvider? engineProvider = null)
    {
        _engineProvider = engineProvider;
    }

    public override IndexerResource ToResource(IndexerDefinition definition)
    {
        var resource = base.ToResource(definition);

        AddDefinitionFields(resource, definition);

        resource.EnableRss = definition.EnableRss;
        resource.EnableAutomaticSearch = definition.EnableAutomaticSearch;
        resource.EnableInteractiveSearch = definition.EnableInteractiveSearch;
        resource.SupportsRss = definition.SupportsRss;
        resource.SupportsSearch = definition.SupportsSearch;
        resource.Protocol = definition.Protocol;
        resource.Priority = definition.Priority;
        resource.SeasonSearchMaximumSingleEpisodeAge = definition.SeasonSearchMaximumSingleEpisodeAge;
        resource.DownloadClientId = definition.DownloadClientId;

        return resource;
    }

    public override IndexerDefinition ToModel(IndexerResource resource, IndexerDefinition? existingDefinition)
    {
        var definition = base.ToModel(resource, existingDefinition);

        definition.EnableRss = resource.EnableRss;
        definition.EnableAutomaticSearch = resource.EnableAutomaticSearch;
        definition.EnableInteractiveSearch = resource.EnableInteractiveSearch;
        definition.Priority = resource.Priority;
        definition.SeasonSearchMaximumSingleEpisodeAge = resource.SeasonSearchMaximumSingleEpisodeAge;
        definition.DownloadClientId = resource.DownloadClientId;

        ReadDefinitionFields(resource, definition, existingDefinition);

        return definition;
    }

    // An indexer added from a definition file asks for whatever the file says (username, password, cookie and so on).
    // Those fields are not part of the settings class, so they are added to the form here and read back in ToModel.
    private void AddDefinitionFields(IndexerResource resource, IndexerDefinition definition)
    {
        if (_engineProvider == null || definition.Settings is not CardigannSettings settings)
        {
            return;
        }

        var template = _engineProvider.FindTemplate(settings.DefinitionFile);
        var order = resource.Fields.Count;

        foreach (var extra in template?.ExtraFields ?? [])
        {
            if (extra.Type.StartsWith("info", StringComparison.OrdinalIgnoreCase) || extra.Type == "cardigannCaptcha")
            {
                continue;
            }

            var isPassword = extra.Type == "password";
            var isCheckbox = extra.Type == "checkbox";
            var stored = settings.ExtraFieldData?.GetValueOrDefault(extra.Name);
            var value = stored ?? (isCheckbox ? string.Equals(extra.Default, "true", StringComparison.OrdinalIgnoreCase) : extra.Default);

            if (isPassword && value is string { Length: > 0 })
            {
                value = PrivateValue;
            }

            resource.Fields.Add(new Field
            {
                Order = order++,
                Name = ExtraPrefix + extra.Name,
                Label = extra.Label.IsNullOrWhiteSpace() ? extra.Name : extra.Label,
                Type = isPassword ? "password" : isCheckbox ? "checkbox" : "textbox",
                Privacy = isPassword ? PrivacyLevel.Password : PrivacyLevel.Normal,
                HelpText = extra.Type == "select" ? "One of: " + string.Join(", ", extra.Options?.Keys.ToList() ?? []) : null,
                Value = value
            });
        }
    }

    private static void ReadDefinitionFields(IndexerResource resource, IndexerDefinition definition, IndexerDefinition? existing)
    {
        if (definition.Settings is not CardigannSettings settings)
        {
            return;
        }

        var existingData = (existing?.Settings as CardigannSettings)?.ExtraFieldData ?? new Dictionary<string, object>();
        var data = new Dictionary<string, object>();

        foreach (var field in resource.Fields.Where(f => f.Name != null && f.Name.StartsWith(ExtraPrefix, StringComparison.Ordinal)))
        {
            var key = field.Name![ExtraPrefix.Length..];
            var value = field.Value;

            // A password the form shows as stars was not changed, so keep the saved one.
            if (value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() == PrivateValue : value as string == PrivateValue)
            {
                if (existingData.TryGetValue(key, out var saved))
                {
                    data[key] = saved;
                }

                continue;
            }

            if (value != null)
            {
                data[key] = value;
            }
        }

        settings.ExtraFieldData = EngineIndexerProvider.NormalizeExtraFields(data);
    }
}
