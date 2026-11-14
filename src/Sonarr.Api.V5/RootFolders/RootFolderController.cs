using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Parser;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.Extensions;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.RootFolders;

[V5ApiController]
public class RootFolderController : RestControllerWithSignalR<RootFolderResource, RootFolder>
{
    private readonly IRootFolderService _rootFolderService;
    private readonly IParsingService _parsingService;

    public RootFolderController(IRootFolderService rootFolderService,
                            IParsingService parsingService,
                            IBroadcastSignalRMessage signalRBroadcaster,
                            RootFolderValidator rootFolderValidator,
                            PathExistsValidator pathExistsValidator,
                            MappedNetworkDriveValidator mappedNetworkDriveValidator,
                            RecycleBinValidator recycleBinValidator,
                            StartupFolderValidator startupFolderValidator,
                            SystemFolderValidator systemFolderValidator,
                            FolderWritableValidator folderWritableValidator)
    : base(signalRBroadcaster)
    {
        _rootFolderService = rootFolderService;
        _parsingService = parsingService;

        SharedValidator.RuleFor(c => c.Path)
            .Cascade(CascadeMode.Stop)
            .IsValidPath()
                       .SetValidator(rootFolderValidator)
                       .SetValidator(mappedNetworkDriveValidator)
                       .SetValidator(startupFolderValidator)
                       .SetValidator(recycleBinValidator)
                       .SetValidator(pathExistsValidator)
                       .SetValidator(systemFolderValidator)
                       .SetValidator(folderWritableValidator);
    }

    protected override RootFolderResource GetResourceById(int id)
    {
        var timeout = Request?.GetBooleanQueryParameter("timeout", true) ?? true;

        return AddMatches(_rootFolderService.Get(id, timeout).ToResource());
    }

    [RestPostById]
    [Consumes("application/json")]
    public Results<Created<RootFolderResource>, NotFound> CreateRootFolder([FromBody] RootFolderResource rootFolderResource)
    {
        var model = rootFolderResource.ToModel();

        return TypedCreated(_rootFolderService.Add(model).Id);
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<RootFolderResource>> GetRootFolders()
    {
        return TypedResults.Ok(_rootFolderService.AllWithUnmappedFolders().ToResource().Select(AddMatches).ToList());
    }

    // Folders named like releases (the usual state of a downloads dump) are matched to the library by their parsed title, so
    // those that belong to a series already there can be merged into it, and the others looked up by the real title.
    private RootFolderResource AddMatches(RootFolderResource resource)
    {
        foreach (var folder in resource.UnmappedFolders)
        {
            if (string.IsNullOrWhiteSpace(folder.Name))
            {
                continue;
            }

            folder.ParsedTitle = Parser.ParseTitle(folder.Name)?.SeriesTitle ?? Parser.ParseSeasonTitle(folder.Name)?.SeriesTitle;
            folder.MatchedSeriesId = _parsingService.GetSeries(folder.Name)?.Id;
        }

        return resource;
    }

    [RestPutById]
    [Consumes("application/json")]
    public Results<Accepted<RootFolderResource>, NotFound> UpdateRootFolder([FromBody] RootFolderResource resource)
    {
        _rootFolderService.SetLibrary(resource.Id, resource.Library);

        return TypedResults.Accepted($"/api/v5/rootfolder/{resource.Id}", GetResourceById(resource.Id));
    }

    [RestDeleteById]
    public NoContent DeleteFolder(int id)
    {
        _rootFolderService.Remove(id);

        return TypedResults.NoContent();
    }
}
