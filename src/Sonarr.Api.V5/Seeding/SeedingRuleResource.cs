using NzbDrone.Core.Download.Engine;

namespace Sonarr.Api.V5.Seeding;

// A seeding goal (roadmap §4.10): the mode, the time in minutes and the ratio.
public class SeedingRuleResource
{
    public EngineSeedingMode Mode { get; set; }
    public int Minutes { get; set; }
    public double Ratio { get; set; }
}

// The body of a PUT: the goal to set, or null to fall back to the next level (season → series → global).
public class SeedingRuleUpdateResource
{
    public SeedingRuleResource? Rule { get; set; }
}

public class SeasonSeedingRuleResource
{
    public int SeasonNumber { get; set; }
    public SeedingRuleResource? Rule { get; set; }
}

public class EpisodeSeedingRuleResource
{
    public int EpisodeId { get; set; }
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public SeedingRuleResource? Rule { get; set; }
}

// Every seeding goal set on a series, its seasons and its episodes (only those that override the next level).
public class SeriesSeedingRulesResource
{
    public int SeriesId { get; set; }
    public SeedingRuleResource? Rule { get; set; }
    public List<SeasonSeedingRuleResource> Seasons { get; set; } = new();
    public List<EpisodeSeedingRuleResource> Episodes { get; set; } = new();
}

public static class SeedingRuleResourceMapper
{
    public static SeedingRuleResource? ToResource(this EngineSeedingRule? model)
    {
        return model == null ? null : new SeedingRuleResource { Mode = model.Mode, Minutes = model.Minutes, Ratio = model.Ratio };
    }

    public static EngineSeedingRule? ToModel(this SeedingRuleResource? resource)
    {
        return resource == null ? null : new EngineSeedingRule { Mode = resource.Mode, Minutes = resource.Minutes, Ratio = resource.Ratio };
    }
}
