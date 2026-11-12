using NzbDrone.Core.Movies.Versions;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Movies;

// A version of a movie: the default one (the movie's own profile, monitored state and file; id 0) or an extra slot.
public class VersionSlotResource : RestResource
{
    public int MovieId { get; set; }
    public string? Label { get; set; }
    public int QualityProfileId { get; set; }
    public bool Monitored { get; set; }
    public bool IsDefault { get; set; }
    public int MovieFileId { get; set; }
}

public static class VersionSlotResourceMapper
{
    public static VersionSlot ToModel(this VersionSlotResource resource)
    {
        return new VersionSlot
        {
            Id = resource.Id,
            MovieId = resource.MovieId,
            Label = resource.Label,
            QualityProfileId = resource.QualityProfileId,
            Monitored = resource.Monitored
        };
    }
}
