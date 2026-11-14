using FluentValidation.Validators;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Validation.Paths
{
    /// <summary>A path has to be inside a root folder that holds this kind of library (or one that holds any).</summary>
    public abstract class LibraryRootFolderValidator : PropertyValidator
    {
        private readonly IRootFolderService _rootFolderService;
        private readonly LibraryType _library;

        protected LibraryRootFolderValidator(IRootFolderService rootFolderService, LibraryType library)
        {
            _rootFolderService = rootFolderService;
            _library = library;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' is in a root folder that holds {other}, not {expected}";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            var path = context.PropertyValue?.ToString();

            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            var root = _rootFolderService.GetBestRootFolder(path);

            if (root == null || root.Library == LibraryType.Any || root.Library == _library)
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("path", path);
            context.MessageFormatter.AppendArgument("other", root.Library.ToString().ToLowerInvariant());
            context.MessageFormatter.AppendArgument("expected", _library.ToString().ToLowerInvariant());

            return false;
        }
    }

    public class SeriesLibraryRootFolderValidator : LibraryRootFolderValidator
    {
        public SeriesLibraryRootFolderValidator(IRootFolderService rootFolderService)
            : base(rootFolderService, LibraryType.Series)
        {
        }
    }

    public class MovieLibraryRootFolderValidator : LibraryRootFolderValidator
    {
        public MovieLibraryRootFolderValidator(IRootFolderService rootFolderService)
            : base(rootFolderService, LibraryType.Movies)
        {
        }
    }
}
