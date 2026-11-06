using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Movies;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Movies;

public class MovieResource : RestResource
{
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? SortTitle { get; set; }
    public Language? OriginalLanguage { get; set; }
    public int Year { get; set; }
    public int TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? Overview { get; set; }
    public MovieStatusType Status { get; set; }
    public List<MediaCover> Images { get; set; } = [];
    public List<string> Genres { get; set; } = [];
    public int Runtime { get; set; }
    public string? Certification { get; set; }
    public DateTime? InCinemas { get; set; }
    public DateTime? PhysicalRelease { get; set; }
    public DateTime? DigitalRelease { get; set; }
    public string? Studio { get; set; }
    public string? Website { get; set; }
    public string? YouTubeTrailerId { get; set; }
    public string? CollectionTitle { get; set; }
    public int CollectionTmdbId { get; set; }

    public string? Path { get; set; }
    public string? RootFolderPath { get; set; }
    public int QualityProfileId { get; set; }
    public bool Monitored { get; set; }
    public MovieStatusType MinimumAvailability { get; set; }
    public HashSet<int> Tags { get; set; } = [];
    public DateTime Added { get; set; }
    public AddMovieOptions? AddOptions { get; set; }
    public DateTime? LastSearchTime { get; set; }

    public bool HasFile { get; set; }
    public int MovieFileId { get; set; }
    public long SizeOnDisk { get; set; }
    public bool IsAvailable { get; set; }
}

public static class MovieResourceMapper
{
    public static MovieResource ToResource(this Movie model)
    {
        var metadata = model.MovieMetadata.Value;

        return new MovieResource
        {
            Id = model.Id,
            Title = metadata.Title,
            OriginalTitle = metadata.OriginalTitle,
            SortTitle = metadata.SortTitle,
            OriginalLanguage = metadata.OriginalLanguage,
            Year = metadata.Year,
            TmdbId = metadata.TmdbId,
            ImdbId = metadata.ImdbId,
            Overview = metadata.Overview,
            Status = metadata.Status,
            Images = metadata.Images ?? [],
            Genres = metadata.Genres ?? [],
            Runtime = metadata.Runtime,
            Certification = metadata.Certification,
            InCinemas = metadata.InCinemas,
            PhysicalRelease = metadata.PhysicalRelease,
            DigitalRelease = metadata.DigitalRelease,
            Studio = metadata.Studio,
            Website = metadata.Website,
            YouTubeTrailerId = metadata.YouTubeTrailerId,
            CollectionTitle = metadata.CollectionTitle,
            CollectionTmdbId = metadata.CollectionTmdbId,

            Path = model.Path,
            RootFolderPath = model.RootFolderPath,
            QualityProfileId = model.QualityProfileId,
            Monitored = model.Monitored,
            MinimumAvailability = model.MinimumAvailability,
            Tags = model.Tags,
            Added = model.Added,
            AddOptions = model.AddOptions,
            LastSearchTime = model.LastSearchTime,

            HasFile = model.HasFile,
            MovieFileId = model.MovieFileId,
            SizeOnDisk = model.MovieFile?.Size ?? 0,
            IsAvailable = model.IsAvailable()
        };
    }

    public static Movie ToModel(this MovieResource resource)
    {
        var model = new Movie
        {
            Id = resource.Id,
            Path = resource.Path ?? string.Empty,
            RootFolderPath = resource.RootFolderPath ?? string.Empty,
            QualityProfileId = resource.QualityProfileId,
            Monitored = resource.Monitored,
            MinimumAvailability = resource.MinimumAvailability,
            Tags = resource.Tags,
            AddOptions = resource.AddOptions
        };

        // The information about the movie itself comes from the metadata service by its TMDb id.
        model.MovieMetadata.Value.TmdbId = resource.TmdbId;
        model.MovieMetadata.Value.Title = resource.Title ?? string.Empty;
        model.MovieMetadata.Value.Year = resource.Year;
        model.MovieMetadata.Value.ImdbId = resource.ImdbId;

        return model;
    }

    // An edit changes only what belongs to the movie in the library.
    public static Movie ToModel(this MovieResource resource, Movie existing)
    {
        existing.Path = resource.Path ?? existing.Path;
        existing.QualityProfileId = resource.QualityProfileId;
        existing.Monitored = resource.Monitored;
        existing.MinimumAvailability = resource.MinimumAvailability;
        existing.Tags = resource.Tags;
        existing.AddOptions = resource.AddOptions;

        return existing;
    }
}
