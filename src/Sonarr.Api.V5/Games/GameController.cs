using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
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

    public GameController(IGameService gameService)
    {
        _gameService = gameService;
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
            Added = game.Added
        };
    }
}
