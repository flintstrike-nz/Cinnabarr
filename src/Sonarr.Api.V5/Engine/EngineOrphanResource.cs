using NzbDrone.Core.Download.Engine;

namespace Sonarr.Api.V5.Engine;

public class EngineOrphanFileResource
{
    public required string Path { get; set; }
    public long Size { get; set; }
    public DateTime LastWrite { get; set; }
}

// Partial download files no engine torrent owns (roadmap §4.13), and how the scheduled cleanup treats them.
public class EngineOrphansResource
{
    public int GraceDays { get; set; }

    // False until a list has been approved: until then the scheduled cleanup only lists the files.
    public bool DeleteAutomatically { get; set; }
    public List<EngineOrphanFileResource> Files { get; set; } = new();
}

// Approves deleting the listed files (those still orphans when deleting); the scheduled cleanup then deletes orphans
// itself, unless DeleteAutomatically is false.
public class EngineOrphanDeleteResource
{
    public List<string> Paths { get; set; } = new();
    public bool DeleteAutomatically { get; set; } = true;
}

public static class EngineOrphanResourceMapper
{
    public static EngineOrphanFileResource ToResource(this EngineOrphanFile model)
    {
        return new EngineOrphanFileResource { Path = model.Path, Size = model.Size, LastWrite = model.LastWrite };
    }
}
