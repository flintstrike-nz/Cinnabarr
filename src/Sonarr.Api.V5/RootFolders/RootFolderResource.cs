using NzbDrone.Common.Extensions;
using NzbDrone.Core.RootFolders;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.RootFolders;

public class RootFolderResource : RestResource
{
    public string? Path { get; set; }
    public bool Accessible { get; set; }
    public bool IsEmpty { get; set; }
    public long? FreeSpace { get; set; }
    public long? TotalSpace { get; set; }

    // What the root folder holds: any, series or movies.
    public LibraryType Library { get; set; }

    public List<UnmappedFolderResource> UnmappedFolders { get; set; } = [];
}

public class UnmappedFolderResource
{
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? RelativePath { get; set; }

    // The series title in the folder's name when it is named like a release, for looking it up.
    public string? ParsedTitle { get; set; }

    // The series in the library that the folder's name belongs to, if there is one.
    public int? MatchedSeriesId { get; set; }
}

public static class RootFolderResourceMapper
{
    public static RootFolderResource ToResource(this RootFolder model)
    {
        return new RootFolderResource
        {
            Id = model.Id,
            Path = model.Path.GetCleanPath(),
            Accessible = model.Accessible,
            IsEmpty = model.IsEmpty,
            FreeSpace = model.FreeSpace,
            TotalSpace = model.TotalSpace,
            Library = model.Library,
            UnmappedFolders = model.UnmappedFolders.Select(f => new UnmappedFolderResource { Name = f.Name, Path = f.Path, RelativePath = f.RelativePath }).ToList()
        };
    }

    public static RootFolder ToModel(this RootFolderResource resource)
    {
        return new RootFolder
        {
            Id = resource.Id,

            Path = resource.Path,
            Library = resource.Library

            // Accessible
            // IsEmpty
            // FreeSpace
            // UnmappedFolders
        };
    }

    public static List<RootFolderResource> ToResource(this IEnumerable<RootFolder> models)
    {
        return models.Select(ToResource).ToList();
    }
}
