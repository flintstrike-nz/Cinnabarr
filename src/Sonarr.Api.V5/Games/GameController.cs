using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Games;
using Sonarr.Http;

namespace Sonarr.Api.V5.Games;

public class GameResource
{
    public int Id { get; set; }
    public required string PlatformKey { get; set; }
    public string? PlatformName { get; set; }
    public required string Title { get; set; }
    public string? SortTitle { get; set; }
    public string? Region { get; set; }
    public string? RootFolderPath { get; set; }
    public string? Path { get; set; }
    public long Size { get; set; }
    public DateTime Added { get; set; }
    public string? Overview { get; set; }
    public int? Year { get; set; }
    public string? Genres { get; set; }
    public string? Developer { get; set; }
    public double? Rating { get; set; }
    public bool HasCover { get; set; }
}

public class GameSettingsResource
{
    public string? IgdbClientId { get; set; }

    // Never sent back; an empty value on save keeps the one already stored.
    public string? IgdbClientSecret { get; set; }
    public bool HasIgdbClientSecret { get; set; }
}

public class GameSettingsTestResource
{
    public bool Ok { get; set; }
    public string? Message { get; set; }
}

public class GamePlatformResource
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string[] Extensions { get; set; }
    public string? Core { get; set; }
}

// The games library: the ROMs found in the Games root folders, and the platforms Cinnabarr knows. Scanning is the ScanGames command.
[V5ApiController("game")]
public class GameController : Controller
{
    private readonly IGameService _gameService;
    private readonly IGameCoverService _coverService;
    private readonly IIgdbClient _igdb;
    private readonly IConfigService _configService;

    public GameController(IGameService gameService, IGameCoverService coverService, IIgdbClient igdb, IConfigService configService)
    {
        _gameService = gameService;
        _coverService = coverService;
        _igdb = igdb;
        _configService = configService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<GameResource>> GetGames()
    {
        return TypedResults.Ok(_gameService.GetAll().Select(ToResource).OrderBy(g => g.SortTitle).ToList());
    }

    [HttpGet("{id:int}")]
    [Produces("application/json")]
    public Ok<GameResource> GetGame([FromRoute] int id)
    {
        return TypedResults.Ok(ToResource(_gameService.Get(id)));
    }

    [HttpDelete("{id:int}")]
    public Ok DeleteGame([FromRoute] int id, [FromQuery] bool deleteFile = false)
    {
        _gameService.Delete(id, deleteFile);

        return TypedResults.Ok();
    }

    [HttpGet("{id:int}/cover")]
    public IActionResult GetCover([FromRoute] int id)
    {
        var path = _coverService.GetCoverPath(_gameService.Get(id));

        return path == null ? NotFound() : PhysicalFile(path, "image/jpeg");
    }

    [HttpGet("settings")]
    [Produces("application/json")]
    public Ok<GameSettingsResource> GetSettings()
    {
        return TypedResults.Ok(new GameSettingsResource
        {
            IgdbClientId = _configService.IgdbClientId,
            HasIgdbClientSecret = !string.IsNullOrEmpty(_configService.IgdbClientSecret)
        });
    }

    [HttpPut("settings")]
    [Produces("application/json")]
    public Ok<GameSettingsResource> PutSettings([FromBody] GameSettingsResource resource)
    {
        _configService.IgdbClientId = resource.IgdbClientId?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(resource.IgdbClientSecret))
        {
            _configService.IgdbClientSecret = resource.IgdbClientSecret.Trim();
        }

        return GetSettings();
    }

    [HttpPost("settings/test")]
    [Produces("application/json")]
    public Ok<GameSettingsTestResource> TestSettings()
    {
        var message = _igdb.TestConnection();

        return TypedResults.Ok(new GameSettingsTestResource { Ok = string.IsNullOrEmpty(message), Message = message });
    }

    [HttpGet("platform")]
    [Produces("application/json")]
    public Ok<List<GamePlatformResource>> GetPlatforms()
    {
        return TypedResults.Ok(GamePlatforms.All.Select(p => new GamePlatformResource
        {
            Key = p.Key,
            Name = p.Name,
            Extensions = p.Extensions,
            Core = p.Core
        }).ToList());
    }

    private static GameResource ToResource(Game game)
    {
        return new GameResource
        {
            Id = game.Id,
            PlatformKey = game.PlatformKey,
            PlatformName = GamePlatforms.Get(game.PlatformKey)?.Name,
            Title = game.Title,
            SortTitle = game.SortTitle,
            Region = game.Region,
            RootFolderPath = game.RootFolderPath,
            Path = game.Path,
            Size = game.Size,
            Added = game.Added,
            Overview = game.Overview,
            Year = game.Year,
            Genres = game.Genres,
            Developer = game.Developer,
            Rating = game.Rating,
            HasCover = !string.IsNullOrEmpty(game.CoverUrl)
        };
    }
}
