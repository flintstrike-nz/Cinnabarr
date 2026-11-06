using System;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.CustomFormats
{
    public class QualityModifierSpecificationValidator : AbstractValidator<QualityModifierSpecification>
    {
        public QualityModifierSpecificationValidator()
        {
            RuleFor(c => c.Value).Custom((value, context) =>
            {
                if (!Enum.IsDefined(typeof(QualityModifier), value))
                {
                    context.AddFailure($"Invalid quality modifier condition value: {value}");
                }
            });
        }
    }

    public class QualityModifierSpecification : CustomFormatSpecificationBase
    {
        private static readonly QualityModifierSpecificationValidator Validator = new();

        public override int Order => 12;
        public override string ImplementationName => "Quality Modifier";

        [FieldDefinition(1, Label = "CustomFormatsSpecificationQualityModifier", Type = FieldType.Select, SelectOptions = typeof(QualityModifier))]
        public int Value { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            var quality = input.MovieInfo?.Quality?.Quality ?? input.EpisodeInfo?.Quality?.Quality;

            return ModifierOf(quality) == (QualityModifier)Value;
        }

        public static QualityModifier ModifierOf(Quality quality)
        {
            if (quality == null)
            {
                return QualityModifier.None;
            }

            if (quality == Quality.RAWHD)
            {
                return QualityModifier.RawHd;
            }

            return quality.Source switch
            {
                QualitySource.DvdRegional => QualityModifier.Regional,
                QualitySource.DvdScreener => QualityModifier.Screener,
                QualitySource.BlurayDisk => QualityModifier.BrDisk,
                QualitySource.BlurayRaw or QualitySource.DvdRaw => QualityModifier.Remux,
                _ => QualityModifier.None
            };
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
