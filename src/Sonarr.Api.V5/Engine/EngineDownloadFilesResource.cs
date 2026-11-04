using NzbDrone.Core.Download.Engine;

namespace Sonarr.Api.V5.Engine;

public class EngineSeedingGoalResource
{
    public EngineSeedingMode Mode { get; set; }
    public int Minutes { get; set; }
    public double Ratio { get; set; }
    public required string Source { get; set; }
    public bool IsMet { get; set; }
}

public class EngineDownloadFileResource
{
    public int Index { get; set; }
    public required string Path { get; set; }
    public bool IsSubtitle { get; set; }
    public EngineLinkFileState State { get; set; }
    public double? Progress { get; set; }
    public DateTime? ImportedAt { get; set; }
    public List<EngineSeedingGoalResource> Goals { get; set; } = new();
}

public class EngineDownloadFilesResource
{
    public bool InEngine { get; set; }
    public bool IsPaused { get; set; }
    public bool IsForceStarted { get; set; }
    public bool IsAwaitingPreparation { get; set; }
    public double? Ratio { get; set; }
    public List<EngineDownloadFileResource> Files { get; set; } = new();
}

public static class EngineDownloadFilesResourceMapper
{
    public static EngineDownloadFilesResource ToResource(this EngineDownloadFiles model)
    {
        return new EngineDownloadFilesResource
        {
            InEngine = model.InEngine,
            IsPaused = model.IsPaused,
            IsForceStarted = model.IsForceStarted,
            IsAwaitingPreparation = model.IsAwaitingPreparation,
            Ratio = model.Ratio,
            Files = model.Files.Select(f => new EngineDownloadFileResource
            {
                Index = f.Index,
                Path = f.Path,
                IsSubtitle = f.IsSubtitle,
                State = f.State,
                Progress = f.Progress,
                ImportedAt = f.ImportedAt,
                Goals = f.Goals.Select(g => new EngineSeedingGoalResource
                {
                    Mode = g.Mode,
                    Minutes = g.Minutes,
                    Ratio = g.Ratio,
                    Source = g.Source ?? "global",
                    IsMet = g.IsMet
                }).ToList()
            }).ToList()
        };
    }
}
