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
    private readonly IGamePlayService _playService;

    public GameController(IGameService gameService, IGameCoverService coverService, IIgdbClient igdb, IConfigService configService, IGamePlayService playService)
    {
        _playService = playService;
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

    [HttpGet("{id:int}/play")]
    [Produces("application/json")]
    public Ok<GamePlayInfo> GetPlayInfo([FromRoute] int id)
    {
        return TypedResults.Ok(_playService.GetInfo(_gameService.Get(id)));
    }

    // The game file for the browser emulator. A disc sheet (.cue) is sent zipped with its tracks.
    [HttpGet("{id:int}/rom")]
    [HttpHead("{id:int}/rom")]
    public IActionResult GetRom([FromRoute] int id)
    {
        var game = _gameService.Get(id);
        var path = _playService.GetPlayablePath(game);

        return PhysicalFile(path, "application/octet-stream", enableRangeProcessing: true);
    }

    [HttpGet("{id:int}/state")]
    public IActionResult GetState([FromRoute] int id)
    {
        var path = _playService.FindState(_gameService.Get(id));

        return path == null ? NotFound() : PhysicalFile(path, "application/octet-stream");
    }

    // The player's save state, kept on the server so it follows them from one device to another.
    [HttpPut("{id:int}/state")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> PutState([FromRoute] int id)
    {
        var saved = await _playService.SaveStateAsync(_gameService.Get(id), Request.Body);

        return saved ? Ok() : BadRequest("The save state was empty.");
    }

    // A BIOS the owner put in <config>/bios/<platform>.bin. Cinnabarr does not ship any.
    [HttpGet("bios/{platformKey}")]
    public IActionResult GetBios([FromRoute] string platformKey)
    {
        var path = _playService.FindBios(platformKey.ToLowerInvariant());

        return path == null ? NotFound() : PhysicalFile(path, "application/octet-stream");
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
