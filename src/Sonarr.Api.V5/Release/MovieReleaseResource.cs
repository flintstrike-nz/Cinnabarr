using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using Sonarr.Api.V5.CustomFormats;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Release;

public class MovieReleaseResource : RestResource
{
    public ParsedMovieInfoResource? ParsedInfo { get; set; }
    public ReleaseInfoResource? Release { get; set; }
    public ReleaseDecisionResource? Decision { get; set; }
    public ReleaseHistoryResource? History { get; set; }
    public int QualityWeight { get; set; }
    public List<Language> Languages { get; set; } = [];
    public int? MappedMovieId { get; set; }
    public bool DownloadAllowed { get; set; }
    public int ReleaseWeight { get; set; }
    public List<CustomFormatResource>? CustomFormats { get; set; }
    public int CustomFormatScore { get; set; }
}

public class ParsedMovieInfoResource
{
    public string? MovieTitle { get; set; }
    public string? ReleaseTitle { get; set; }
    public int Year { get; set; }
    public string? Edition { get; set; }
    public string? ReleaseGroup { get; set; }
    public QualityModel? Quality { get; set; }
    public List<Language> Languages { get; set; } = [];
}

public class MovieReleaseGrabResource
{
    public required string Guid { get; set; }
    public required int IndexerId { get; set; }
    public int? DownloadClientId { get; set; }
}

public static class MovieReleaseResourceMapper
{
    public static MovieReleaseResource ToResource(this DownloadDecision model, int weight, QualityProfile profile)
    {
        var remote = model.RemoteMovie;
        var parsed = remote.ParsedMovieInfo;

        var resource = new MovieReleaseResource
        {
            ParsedInfo = parsed == null
                ? null
                : new ParsedMovieInfoResource
                {
                    MovieTitle = parsed.PrimaryMovieTitle,
                    ReleaseTitle = parsed.ReleaseTitle,
                    Year = parsed.Year,
                    Edition = parsed.Edition,
                    ReleaseGroup = parsed.ReleaseGroup,
                    Quality = parsed.Quality,
                    Languages = parsed.Languages ?? []
                },
            Release = remote.Release.ToResource(),
            Decision = new ReleaseDecisionResource(model),
            Languages = remote.Languages,
            MappedMovieId = remote.Movie?.Id,
            DownloadAllowed = remote.DownloadAllowed,
            ReleaseWeight = weight,
            CustomFormatScore = remote.CustomFormatScore,
            CustomFormats = remote.CustomFormats?.ToResource(false)
        };

        if (parsed?.Quality != null)
        {
            resource.QualityWeight = profile.GetIndex(parsed.Quality.Quality).Index * 100;
            resource.QualityWeight += parsed.Quality.Revision.Real * 10;
            resource.QualityWeight += parsed.Quality.Revision.Version;
        }

        return resource;
    }
}
