using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.Movies
{
    public class MovieCustomSettingsValidator : AbstractValidator<MovieCustomSettings>
    {
        public MovieCustomSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
        }
    }

    public class MovieCustomSettings : ImportListSettingsBase<MovieCustomSettings>
    {
        private static readonly MovieCustomSettingsValidator Validator = new();

        [FieldDefinition(0, Label = "ImportListsCustomListSettingsUrl", HelpText = "ImportListsMovieCustomListSettingsUrlHelpText")]
        public override string BaseUrl { get; set; } = string.Empty;

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
