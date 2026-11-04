using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Tv;
using Sonarr.Http;

namespace Sonarr.Api.V5.Seeding;

// Seeding goals for a series, a season or an episode (roadmap §4.10). Each overrides the next level up (episode →
// season → series → the global goal in the download client settings); a private tracker's minimum always applies too.
[V5ApiController("seeding")]
public class SeedingController : Controller
{
    private readonly ISeriesService _seriesService;
    private readonly IEpisodeService _episodeService;

    public SeedingController(ISeriesService seriesService, IEpisodeService episodeService)
    {
        _seriesService = seriesService;
        _episodeService = episodeService;
    }

    [HttpGet("series/{seriesId:int}")]
    [Produces("application/json")]
    public SeriesSeedingRulesResource GetSeries([FromRoute] int seriesId)
    {
        var series = _seriesService.GetSeries(seriesId);

        return new SeriesSeedingRulesResource
        {
            SeriesId = series.Id,
            Rule = series.SeedingRule.ToResource(),
            Seasons = series.Seasons
                .Where(s => s.SeedingRule != null)
                .Select(s => new SeasonSeedingRuleResource { SeasonNumber = s.SeasonNumber, Rule = s.SeedingRule.ToResource() })
                .ToList(),
            Episodes = _episodeService.GetEpisodeBySeries(seriesId)
                .Where(e => e.SeedingRule != null)
                .Select(e => new EpisodeSeedingRuleResource { EpisodeId = e.Id, SeasonNumber = e.SeasonNumber, EpisodeNumber = e.EpisodeNumber, Rule = e.SeedingRule.ToResource() })
                .ToList()
        };
    }

    [HttpPut("series/{seriesId:int}")]
    [Consumes("application/json")]
    public NoContent SetSeries([FromRoute] int seriesId, [FromBody] SeedingRuleUpdateResource resource)
    {
        Validate(resource);

        var series = _seriesService.GetSeries(seriesId);
        series.SeedingRule = resource.Rule.ToModel();
        _seriesService.UpdateSeries(series, false);

        return TypedResults.NoContent();
    }

    [HttpPut("series/{seriesId:int}/season/{seasonNumber:int}")]
    [Consumes("application/json")]
    public Results<NoContent, NotFound> SetSeason([FromRoute] int seriesId, [FromRoute] int seasonNumber, [FromBody] SeedingRuleUpdateResource resource)
    {
        Validate(resource);

        var series = _seriesService.GetSeries(seriesId);
        var season = series.Seasons.FirstOrDefault(s => s.SeasonNumber == seasonNumber);

        if (season == null)
        {
            return TypedResults.NotFound();
        }

        season.SeedingRule = resource.Rule.ToModel();
        _seriesService.UpdateSeries(series, false);

        return TypedResults.NoContent();
    }

    [HttpPut("episode/{episodeId:int}")]
    [Consumes("application/json")]
    public NoContent SetEpisode([FromRoute] int episodeId, [FromBody] SeedingRuleUpdateResource resource)
    {
        Validate(resource);

        var episode = _episodeService.GetEpisode(episodeId);
        episode.SeedingRule = resource.Rule.ToModel();
        _episodeService.UpdateEpisode(episode);

        return TypedResults.NoContent();
    }

    // Only the modes this version knows, a time from zero up and a ratio from zero up.
    private static void Validate(SeedingRuleUpdateResource resource)
    {
        var rule = resource.Rule;

        if (rule == null)
        {
            return;
        }

        var failures = new List<ValidationFailure>();

        if (!Enum.IsDefined(rule.Mode))
        {
            failures.Add(new ValidationFailure("rule.mode", $"Unknown seeding mode {(int)rule.Mode}"));
        }

        if (rule.Minutes < 0)
        {
            failures.Add(new ValidationFailure("rule.minutes", "The seeding time can't be negative"));
        }

        if (rule.Ratio < 0 || double.IsNaN(rule.Ratio) || double.IsInfinity(rule.Ratio))
        {
            failures.Add(new ValidationFailure("rule.ratio", "The seeding ratio must be a number from 0 up"));
        }

        // Sent back as the fields and reasons, so the caller can tell what to fix.
        if (failures.Any())
        {
            throw new ValidationException(failures);
        }
    }
}
