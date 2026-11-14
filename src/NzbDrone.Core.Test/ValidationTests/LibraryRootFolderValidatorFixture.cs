using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ValidationTests
{
    public class LibraryRootFolderValidatorFixture : CoreTest<SeriesLibraryRootFolderValidator>
    {
        private TestValidator<Series> _validator;

        [SetUp]
        public void Setup()
        {
            _validator = new TestValidator<Series>
                            {
                                v => v.RuleFor(s => s.Path).SetValidator(Subject)
                            };
        }

        private void GivenRoot(LibraryType? library)
        {
            var root = library.HasValue ? new RootFolder { Path = @"C:\Media".AsOsAgnostic(), Library = library.Value } : null;

            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.GetBestRootFolder(It.IsAny<string>()))
                  .Returns(root);
        }

        private bool IsValid()
        {
            var series = Builder<Series>.CreateNew()
                                        .With(s => s.Path = @"C:\Media\Show".AsOsAgnostic())
                                        .Build();

            return _validator.Validate(series).IsValid;
        }

        [Test]
        public void should_be_valid_when_no_root_folder_matches()
        {
            GivenRoot(null);
            IsValid().Should().BeTrue();
        }

        [TestCase(LibraryType.Any)]
        [TestCase(LibraryType.Series)]
        public void should_be_valid_when_root_holds_series_or_any(LibraryType library)
        {
            GivenRoot(library);
            IsValid().Should().BeTrue();
        }

        [Test]
        public void should_not_be_valid_when_root_holds_movies()
        {
            GivenRoot(LibraryType.Movies);
            IsValid().Should().BeFalse();
        }
    }
}
