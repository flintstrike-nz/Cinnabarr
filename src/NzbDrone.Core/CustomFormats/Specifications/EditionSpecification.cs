namespace NzbDrone.Core.CustomFormats
{
    public class EditionSpecification : RegexSpecificationBase
    {
        public override int Order => 11;
        public override string ImplementationName => "Edition";

        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            return MatchString(input.MovieInfo?.Edition);
        }
    }
}
