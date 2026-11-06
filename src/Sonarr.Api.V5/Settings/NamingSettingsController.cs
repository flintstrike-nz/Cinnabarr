using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Settings;

[V5ApiController("settings/naming")]
public class NamingSettingsController : RestController<NamingSettingsResource>
{
    private readonly INamingConfigService _namingConfigService;
    private readonly IFilenameSampleService _filenameSampleService;
    private readonly IFilenameValidationService _filenameValidationService;
    private readonly IBuildMovieFileNames _movieFileNameBuilder;

    public NamingSettingsController(INamingConfigService namingConfigService,
                              IFilenameSampleService filenameSampleService,
                              IFilenameValidationService filenameValidationService,
                              IBuildMovieFileNames movieFileNameBuilder)
    {
        _movieFileNameBuilder = movieFileNameBuilder;
        _namingConfigService = namingConfigService;
        _filenameSampleService = filenameSampleService;
        _filenameValidationService = filenameValidationService;

        SharedValidator.RuleFor(c => c.MultiEpisodeStyle).InclusiveBetween(0, 5);
        SharedValidator.RuleFor(c => c.StandardEpisodeFormat).ValidEpisodeFormat();
        SharedValidator.RuleFor(c => c.DailyEpisodeFormat).ValidDailyEpisodeFormat();
        SharedValidator.RuleFor(c => c.AnimeEpisodeFormat).ValidAnimeEpisodeFormat();
        SharedValidator.RuleFor(c => c.SeriesFolderFormat).ValidSeriesFolderFormat();
        SharedValidator.RuleFor(c => c.SeasonFolderFormat).ValidSeasonFolderFormat();
        SharedValidator.RuleFor(c => c.SpecialsFolderFormat).ValidSpecialsFolderFormat();
        SharedValidator.RuleFor(c => c.StandardMovieFormat).Must(IsValidMovieFileFormat)
            .WithMessage("Movie format must be set and include the movie title, for example {Movie Title} ({Release Year}) {Quality Full}")
            .When(c => c.RenameMovies);
        SharedValidator.RuleFor(c => c.MovieFolderFormat).Must(IsValidMovieFolderFormat)
            .WithMessage("Movie folder format must be set and include the movie title, for example {Movie Title} ({Release Year})");
        SharedValidator.RuleFor(c => c.CustomColonReplacementFormat).ValidCustomColonReplacement().When(c => c.ColonReplacementFormat == (int)ColonReplacementFormat.Custom);
    }

    protected override NamingSettingsResource GetResourceById(int id)
    {
        return _namingConfigService.GetConfig().ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<NamingSettingsResource> GetNamingConfig()
    {
        return TypedResults.Ok(GetResourceById(1));
    }

    [RestPutById]
    [Consumes("application/json")]
    public Results<Accepted<NamingSettingsResource>, NotFound> UpdateNamingConfig([FromBody] NamingSettingsResource resource)
    {
        var nameSpec = resource.ToModel();
        ValidateFormatResult(nameSpec);

        _namingConfigService.Save(nameSpec);

        return TypedAccepted(resource.Id);
    }

    [HttpGet("examples")]
    [Produces("application/json")]
    public Ok<NamingExampleResource> GetExamples([FromQuery] NamingSettingsResource settings)
    {
        if (settings.Id == 0)
        {
            settings = GetResourceById(1);
        }

        var nameSpec = settings.ToModel();
        var sampleResource = new NamingExampleResource();

        var singleEpisodeSampleResult = _filenameSampleService.GetStandardSample(nameSpec);
        var multiEpisodeSampleResult = _filenameSampleService.GetMultiEpisodeSample(nameSpec);
        var dailyEpisodeSampleResult = _filenameSampleService.GetDailySample(nameSpec);
        var animeEpisodeSampleResult = _filenameSampleService.GetAnimeSample(nameSpec);
        var animeMultiEpisodeSampleResult = _filenameSampleService.GetAnimeMultiEpisodeSample(nameSpec);

        sampleResource.SingleEpisodeExample = _filenameValidationService.ValidateStandardFilename(singleEpisodeSampleResult) != null
                ? null
                : singleEpisodeSampleResult.FileName;

        sampleResource.MultiEpisodeExample = _filenameValidationService.ValidateStandardFilename(multiEpisodeSampleResult) != null
                ? null
                : multiEpisodeSampleResult.FileName;

        sampleResource.DailyEpisodeExample = _filenameValidationService.ValidateDailyFilename(dailyEpisodeSampleResult) != null
                ? null
                : dailyEpisodeSampleResult.FileName;

        sampleResource.AnimeEpisodeExample = _filenameValidationService.ValidateAnimeFilename(animeEpisodeSampleResult) != null
                ? null
                : animeEpisodeSampleResult.FileName;

        sampleResource.AnimeMultiEpisodeExample = _filenameValidationService.ValidateAnimeFilename(animeMultiEpisodeSampleResult) != null
                ? null
                : animeMultiEpisodeSampleResult.FileName;

        sampleResource.MovieExample = BuildMovieExample(nameSpec);
        sampleResource.MovieFolderExample = nameSpec.MovieFolderFormat.IsNullOrWhiteSpace()
            ? null
            : _movieFileNameBuilder.GetMovieFolder(SampleMovie(), nameSpec);

        sampleResource.SeriesFolderExample = nameSpec.SeriesFolderFormat.IsNullOrWhiteSpace()
            ? null
            : _filenameSampleService.GetSeriesFolderSample(nameSpec);

        sampleResource.SeasonFolderExample = nameSpec.SeasonFolderFormat.IsNullOrWhiteSpace()
            ? null
            : _filenameSampleService.GetSeasonFolderSample(nameSpec);

        sampleResource.SpecialsFolderExample = nameSpec.SpecialsFolderFormat.IsNullOrWhiteSpace()
            ? null
            : _filenameSampleService.GetSpecialsFolderSample(nameSpec);

        return TypedResults.Ok(sampleResource);
    }

    private static bool IsValidMovieFileFormat(string? format)
    {
        return format.IsNotNullOrWhiteSpace() && FileNameBuilder.MovieTitleRegex.IsMatch(format);
    }

    private static bool IsValidMovieFolderFormat(string? format)
    {
        return format.IsNotNullOrWhiteSpace() && FileNameBuilder.MovieTitleRegex.IsMatch(format);
    }

    private static Movie SampleMovie()
    {
        return new Movie
        {
            Path = "/movies/The Matrix (1999)",
            MovieMetadata = new MovieMetadata
            {
                Title = "The Matrix",
                OriginalTitle = "The Matrix",
                Year = 1999,
                ImdbId = "tt0133093",
                TmdbId = 603
            }
        };
    }

    // The renamed example is shown even while renaming is off, as that is what turning it on would give.
    private string? BuildMovieExample(NamingConfig nameSpec)
    {
        if (!IsValidMovieFileFormat(nameSpec.StandardMovieFormat))
        {
            return null;
        }

        var movie = SampleMovie();
        var file = new MovieFile
        {
            Quality = new QualityModel(Quality.Bluray1080p),
            ReleaseGroup = "RlsGrp",
            SceneName = "The.Matrix.1999.1080p.BluRay.x264-RlsGrp",
            RelativePath = "The.Matrix.1999.1080p.BluRay.x264-RlsGrp.mkv"
        };

        var sample = new NamingConfig
        {
            RenameMovies = true,
            ReplaceIllegalCharacters = nameSpec.ReplaceIllegalCharacters,
            ColonReplacementFormat = nameSpec.ColonReplacementFormat,
            CustomColonReplacementFormat = nameSpec.CustomColonReplacementFormat,
            StandardMovieFormat = nameSpec.StandardMovieFormat,
            MovieFolderFormat = nameSpec.MovieFolderFormat
        };

        try
        {
            return _movieFileNameBuilder.BuildFileName(movie, file, sample, new List<NzbDrone.Core.CustomFormats.CustomFormat>());
        }
        catch (NamingFormatException)
        {
            return null;
        }
    }

    private void ValidateFormatResult(NamingConfig nameSpec)
    {
        var singleEpisodeSampleResult = _filenameSampleService.GetStandardSample(nameSpec);
        var multiEpisodeSampleResult = _filenameSampleService.GetMultiEpisodeSample(nameSpec);
        var dailyEpisodeSampleResult = _filenameSampleService.GetDailySample(nameSpec);
        var animeEpisodeSampleResult = _filenameSampleService.GetAnimeSample(nameSpec);
        var animeMultiEpisodeSampleResult = _filenameSampleService.GetAnimeMultiEpisodeSample(nameSpec);

        var singleEpisodeValidationResult = _filenameValidationService.ValidateStandardFilename(singleEpisodeSampleResult);
        var multiEpisodeValidationResult = _filenameValidationService.ValidateStandardFilename(multiEpisodeSampleResult);
        var dailyEpisodeValidationResult = _filenameValidationService.ValidateDailyFilename(dailyEpisodeSampleResult);
        var animeEpisodeValidationResult = _filenameValidationService.ValidateAnimeFilename(animeEpisodeSampleResult);
        var animeMultiEpisodeValidationResult = _filenameValidationService.ValidateAnimeFilename(animeMultiEpisodeSampleResult);

        var validationFailures = new List<ValidationFailure>();

        validationFailures.AddIfNotNull(singleEpisodeValidationResult);
        validationFailures.AddIfNotNull(multiEpisodeValidationResult);
        validationFailures.AddIfNotNull(dailyEpisodeValidationResult);
        validationFailures.AddIfNotNull(animeEpisodeValidationResult);
        validationFailures.AddIfNotNull(animeMultiEpisodeValidationResult);

        if (validationFailures.Any())
        {
            throw new ValidationException(validationFailures.DistinctBy(v => v.PropertyName).ToArray());
        }
    }
}
