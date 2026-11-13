using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers.Cardigann;
using NzbDrone.Core.Localization;
using NzbDrone.Core.ThingiProvider;
using EngineCardigann = NzbDrone.Core.IndexerEngine.Indexers.Definitions.Cardigann.Cardigann;
using EngineCardigannSettings = NzbDrone.Core.IndexerEngine.Indexers.Definitions.Cardigann.CardigannSettings;
using EngineDefinition = NzbDrone.Core.IndexerEngine.Indexers.IndexerDefinition;
using EngineIndexer = NzbDrone.Core.IndexerEngine.Indexers.IIndexer;
using EngineSettingsField = NzbDrone.Core.IndexerEngine.Indexers.Definitions.Cardigann.SettingsField;
using EngineTorrentSettings = NzbDrone.Core.IndexerEngine.Indexers.ITorrentIndexerSettings;

namespace NzbDrone.Core.Indexers.Engine
{
    public interface IEngineNativeIndexers
    {
        /// <summary>The "definition file" of an indexer that is code in the engine rather than a file: native:&lt;class name&gt;.</summary>
        bool IsNative(string definitionFile);

        /// <summary>Every engine indexer written as code (IPTorrents-style sites with their own API or login).</summary>
        IReadOnlyList<EngineDefinition> Templates();

        EngineDefinition FindTemplate(string definitionFile);

        /// <summary>The engine indexer that runs a saved indexer, with its settings built from what was saved.</summary>
        EngineIndexer Create(IndexerDefinition definition, CardigannSettings settings, EngineDefinition template);

        /// <summary>Keeps settings an engine indexer learned while it ran (a token, an API key) with the saved indexer.</summary>
        void SaveSettings(EngineDefinition engineDefinition);
    }

    /// <summary>
    /// Prowlarr implements many sites as code instead of as definition files. They are hosted like the definition-file
    /// indexers: one entry per site, whose settings fields come from the site's settings class (what the site asks for is
    /// kept as extra field data) and which the same wrapper runs.
    /// </summary>
    public class EngineNativeIndexers : IEngineNativeIndexers
    {
        public const string Prefix = "native:";

        // Handled by the app's own indexers or by the generic entries.
        private static readonly string[] Skipped = { "Cardigann", "Newznab", "Torznab", "TorrentRssIndexer" };

        private static readonly string[] NotFields = { "BaseUrl", "BaseSettings", "TorrentBaseSettings" };

        private readonly IServiceProvider _services;
        private readonly IIndexerRepository _indexerRepository;
        private readonly ILocalizationService _localizationService;
        private readonly ICached<Dictionary<string, EngineDefinition>> _templates;

        public EngineNativeIndexers(IServiceProvider services,
                                    IIndexerRepository indexerRepository,
                                    ILocalizationService localizationService,
                                    ICacheManager cacheManager)
        {
            _services = services;
            _indexerRepository = indexerRepository;
            _localizationService = localizationService;
            _templates = cacheManager.GetCache<Dictionary<string, EngineDefinition>>(GetType(), "templates");
        }

        public bool IsNative(string definitionFile)
        {
            return definitionFile != null && definitionFile.StartsWith(Prefix, StringComparison.Ordinal);
        }

        public IReadOnlyList<EngineDefinition> Templates()
        {
            return Loaded().Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public EngineDefinition FindTemplate(string definitionFile)
        {
            return IsNative(definitionFile) && Loaded().TryGetValue(definitionFile, out var template) ? template : null;
        }

        public EngineIndexer Create(IndexerDefinition definition, CardigannSettings settings, EngineDefinition template)
        {
            var type = FindType(settings.DefinitionFile) ?? throw new NotSupportedException($"Indexer '{settings.DefinitionFile}' is not available");
            var indexer = (EngineIndexer)_services.GetRequiredService(type);

            var engineSettings = BuildSettings(indexer.ConfigContract, settings.BaseUrl ?? template.IndexerUrls?.FirstOrDefault(), EngineIndexerProvider.NormalizeExtraFields(settings.ExtraFieldData), template.ExtraFields);

            if (engineSettings is EngineTorrentSettings torrent)
            {
                torrent.TorrentBaseSettings.AppMinimumSeeders = settings.MinimumSeeders;
            }

            indexer.Definition = new EngineDefinition
            {
                Id = definition.Id,
                Name = definition.Name,
                Enable = definition.Enable,
                Implementation = type.Name,
                ConfigContract = indexer.ConfigContract.Name,
                Tags = definition.Tags,
                Priority = definition.Priority,
                Settings = (IProviderConfig)engineSettings,
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

            return indexer;
        }

        public void SaveSettings(EngineDefinition engineDefinition)
        {
            if (engineDefinition?.Settings == null || engineDefinition.Id == 0)
            {
                return;
            }

            IndexerDefinition stored;

            try
            {
                stored = _indexerRepository.Get(engineDefinition.Id);
            }
            catch (ModelNotFoundException)
            {
                return;
            }

            if (stored.Settings is not CardigannSettings settings)
            {
                return;
            }

            var data = EngineIndexerProvider.NormalizeExtraFields(settings.ExtraFieldData);

            foreach (var property in FieldProperties(engineDefinition.Settings.GetType()))
            {
                var value = property.GetValue(engineDefinition.Settings);

                data[property.Name] = value is IEnumerable<int> numbers ? string.Join(",", numbers) : value;
            }

            settings.ExtraFieldData = data;
            _indexerRepository.Update(stored);
        }

        private Dictionary<string, EngineDefinition> Loaded()
        {
            var templates = _templates.Get("all", LoadTemplates, TimeSpan.FromMinutes(30));

            if (templates.Count == 0)
            {
                _templates.Remove("all");
            }

            return templates;
        }

        private Type FindType(string definitionFile)
        {
            return IsNative(definitionFile)
                ? NativeTypes().FirstOrDefault(t => string.Equals(Prefix + t.Name, definitionFile, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        private static IEnumerable<Type> NativeTypes()
        {
            return typeof(EngineCardigann).Assembly.GetTypes()
                .Where(t => t is { IsAbstract: false, IsClass: true } &&
                            typeof(EngineIndexer).IsAssignableFrom(t) &&
                            t.Namespace != null &&
                            t.Namespace.StartsWith("NzbDrone.Core.IndexerEngine.Indexers.Definitions", StringComparison.Ordinal) &&
                            !Skipped.Contains(t.Name));
        }

        private Dictionary<string, EngineDefinition> LoadTemplates()
        {
            var result = new Dictionary<string, EngineDefinition>(StringComparer.OrdinalIgnoreCase);

            foreach (var type in NativeTypes())
            {
                try
                {
                    var indexer = (EngineIndexer)_services.GetRequiredService(type);
                    var defaults = Activator.CreateInstance(indexer.ConfigContract);

                    result[Prefix + type.Name] = new EngineDefinition
                    {
                        Name = indexer.Name ?? type.Name,
                        Implementation = type.Name,
                        ConfigContract = indexer.ConfigContract.Name,
                        Settings = new EngineCardigannSettings { DefinitionFile = Prefix + type.Name },
                        IndexerUrls = indexer.IndexerUrls,
                        LegacyUrls = indexer.LegacyUrls,
                        Description = indexer.Description,
                        Language = indexer.Language,
                        Protocol = indexer.Protocol,
                        Privacy = indexer.Privacy,
                        SupportsRss = indexer.SupportsRss,
                        SupportsSearch = indexer.SupportsSearch,
                        SupportsRedirect = indexer.SupportsRedirect,
                        SupportsPagination = indexer.SupportsPagination,
                        Capabilities = indexer.Capabilities,
                        Encoding = indexer.Encoding,
                        ExtraFields = Describe(indexer.ConfigContract, defaults)
                    };
                }
                catch (Exception)
                {
                    // An indexer that can't be created is left out of the list rather than breaking the whole list.
                }
            }

            return result;
        }

        // The settings the site asks for: its own properties that have a field definition.
        private static IEnumerable<PropertyInfo> FieldProperties(Type settingsType)
        {
            return settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite &&
                            !NotFields.Contains(p.Name) &&
                            p.GetCustomAttribute<FieldDefinitionAttribute>() is { Hidden: not HiddenType.Hidden } &&
                            IsSimple(p.PropertyType))
                .OrderBy(p => p.GetCustomAttribute<FieldDefinitionAttribute>().Order);
        }

        private static bool IsSimple(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            return type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(long) || type == typeof(double) ||
                   type.IsEnum || typeof(IEnumerable<int>).IsAssignableFrom(type);
        }

        private List<EngineSettingsField> Describe(Type settingsType, object defaults)
        {
            var fields = new List<EngineSettingsField>();

            foreach (var property in FieldProperties(settingsType))
            {
                var attribute = property.GetCustomAttribute<FieldDefinitionAttribute>();
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var value = property.GetValue(defaults);

                var field = new EngineSettingsField
                {
                    Name = property.Name,
                    Label = attribute.Label == null ? property.Name : _localizationService.GetLocalizedString(attribute.Label),
                    Type = attribute.Type switch
                    {
                        FieldType.Password => "password",
                        _ when attribute.Privacy is PrivacyLevel.Password or PrivacyLevel.ApiKey => "password",
                        FieldType.Checkbox => "checkbox",
                        FieldType.Select => "select",
                        _ => "text"
                    },
                    Default = value is IEnumerable<int> numbers ? string.Join(",", numbers) : value?.ToString()
                };

                if (type.IsEnum)
                {
                    field.Type = "select";
                    field.Options = Enum.GetNames(type).ToDictionary(n => n, n => OptionLabel(type, n));
                }

                if (type == typeof(bool))
                {
                    field.Type = "checkbox";
                    field.Default = (value is true).ToString().ToLowerInvariant();
                }

                fields.Add(field);
            }

            return fields;
        }

        private static string OptionLabel(Type enumType, string name)
        {
            return enumType.GetField(name)?.GetCustomAttribute<FieldOptionAttribute>()?.Label ?? name;
        }

        private static object BuildSettings(Type settingsType, string baseUrl, Dictionary<string, object> data, IEnumerable<EngineSettingsField> fields)
        {
            var settings = Activator.CreateInstance(settingsType);

            settingsType.GetProperty("BaseUrl")?.SetValue(settings, baseUrl);

            foreach (var property in FieldProperties(settingsType))
            {
                if (!data.TryGetValue(property.Name, out var raw) || raw == null)
                {
                    continue;
                }

                if (TryConvert(raw, property.PropertyType, out var value))
                {
                    property.SetValue(settings, value);
                }
            }

            return settings;
        }

        private static bool TryConvert(object raw, Type target, out object value)
        {
            target = Nullable.GetUnderlyingType(target) ?? target;
            var text = Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

            try
            {
                if (target == typeof(string))
                {
                    value = text;
                }
                else if (target == typeof(bool))
                {
                    value = raw is bool b ? b : string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
                }
                else if (target == typeof(int))
                {
                    value = int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (target == typeof(long))
                {
                    value = long.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (target == typeof(double))
                {
                    value = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (target.IsEnum)
                {
                    value = Enum.Parse(target, text, true);
                }
                else
                {
                    var numbers = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                      .Select(s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture));

                    value = target.IsArray ? numbers.ToArray() : numbers.ToList();
                }

                return true;
            }
            catch (Exception)
            {
                value = null;

                return false;
            }
        }
    }

    /// <summary>The engine's view of the saved indexers (see <see cref="IEngineNativeIndexers.SaveSettings"/>).</summary>
    public class EngineIndexerRepository : NzbDrone.Core.IndexerEngine.Indexers.IIndexerRepository
    {
        private readonly IEngineNativeIndexers _natives;

        public EngineIndexerRepository(IEngineNativeIndexers natives)
        {
            _natives = natives;
        }

        public void UpdateSettings(EngineDefinition model)
        {
            _natives.SaveSettings(model);
        }
    }
}
