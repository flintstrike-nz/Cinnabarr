using System;
using System.Collections.Generic;
using Equ;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Cardigann
{
    public class CardigannSettingsValidator : AbstractValidator<CardigannSettings>
    {
        public CardigannSettingsValidator()
        {
            RuleFor(c => c.DefinitionFile).NotEmpty();
            RuleFor(c => c.SeedCriteria).SetValidator(_ => new SeedCriteriaSettingsValidator());
        }
    }

    /// <summary>
    /// Settings for an indexer that is described by a definition file. The login fields a definition asks for
    /// (username, password, cookie, captcha and so on) are kept in <see cref="ExtraFieldData"/>.
    /// </summary>
    public class CardigannSettings : PropertywiseEquatable<CardigannSettings>, ITorrentIndexerSettings
    {
        private static readonly CardigannSettingsValidator Validator = new();

        public CardigannSettings()
        {
            MinimumSeeders = IndexerDefaults.MINIMUM_SEEDERS;
            MultiLanguages = Array.Empty<int>();
            FailDownloads = Array.Empty<int>();
            ExtraFieldData = new Dictionary<string, object>();
        }

        [FieldDefinition(0, Hidden = HiddenType.Hidden)]
        public string DefinitionFile { get; set; }

        [FieldDefinition(1, Label = "IndexerSettingsBaseUrl", Type = FieldType.Select, SelectOptionsProviderAction = "getUrls", HelpText = "IndexerSettingsBaseUrlHelpText")]
        public string BaseUrl { get; set; }

        [MemberwiseEqualityIgnore]
        public Dictionary<string, object> ExtraFieldData { get; set; }

        [FieldDefinition(10, Type = FieldType.Number, Label = "IndexerSettingsMinimumSeeders", HelpText = "IndexerSettingsMinimumSeedersHelpText")]
        public int MinimumSeeders { get; set; }

        [FieldDefinition(11)]
        public SeedCriteriaSettings SeedCriteria { get; set; } = new();

        [FieldDefinition(12, Type = FieldType.Checkbox, Label = "IndexerSettingsRejectBlocklistedTorrentHashes", HelpText = "IndexerSettingsRejectBlocklistedTorrentHashesHelpText")]
        public bool RejectBlocklistedTorrentHashesWhileGrabbing { get; set; }

        [FieldDefinition(13, Type = FieldType.Checkbox, Label = "IndexerSettingsRejectTorrentFilesWithBlockedExtensions", HelpText = "IndexerSettingsRejectTorrentFilesWithBlockedExtensionsHelpText")]
        public bool RejectTorrentFilesWithBlockedExtensionsWhileGrabbing { get; set; }

        [FieldDefinition(14, Type = FieldType.Select, SelectOptions = typeof(RealLanguageFieldConverter), Label = "IndexerSettingsMultiLanguageRelease", HelpText = "IndexerSettingsMultiLanguageReleaseHelpText")]
        public IEnumerable<int> MultiLanguages { get; set; }

        [FieldDefinition(15, Type = FieldType.Select, SelectOptions = typeof(FailDownloads), Label = "IndexerSettingsFailDownloads", HelpText = "IndexerSettingsFailDownloadsHelpText")]
        public IEnumerable<int> FailDownloads { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
