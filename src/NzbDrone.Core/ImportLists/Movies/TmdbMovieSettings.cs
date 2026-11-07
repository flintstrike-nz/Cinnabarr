using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.Movies
{
    public class TmdbMovieSettingsValidator : AbstractValidator<TmdbMovieSettings>
    {
        public TmdbMovieSettingsValidator()
        {
            RuleFor(c => c.ListType).IsInEnum();
        }
    }

    public class TmdbMovieSettings : ImportListSettingsBase<TmdbMovieSettings>
    {
        private static readonly TmdbMovieSettingsValidator Validator = new();

        public override string BaseUrl { get; set; } = string.Empty;

        [FieldDefinition(0, Label = "ImportListsTmdbMovieSettingsListType", HelpText = "ImportListsTmdbMovieSettingsListTypeHelpText", Type = FieldType.Select, SelectOptions = typeof(TmdbMovieListType))]
        public int ListType { get; set; } = (int)TmdbMovieListType.Trending;

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
