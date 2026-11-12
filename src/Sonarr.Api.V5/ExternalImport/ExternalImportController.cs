using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ExternalImport;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.ExternalImport;

public class ExternalImportRequest
{
    // "sonarr", "radarr" or "prowlarr".
    public required string Kind { get; set; }

    // The other install's data folder (the one holding its database).
    public required string Folder { get; set; }

    // Paths starting with PathFrom there start with PathTo here.
    public string? PathFrom { get; set; }
    public string? PathTo { get; set; }
}

public class ExternalImportItemResource
{
    public required string Name { get; set; }
    public required string Status { get; set; }
    public string? Reason { get; set; }
}

// Bringing the library (or indexers) of another Sonarr, Radarr or Prowlarr install into Cinnabarr. The import itself
// runs as the ExternalInstallImport command, which can take a while.
[V5ApiController("import/external")]
public class ExternalImportController : Controller
{
    private readonly IExternalInstallImporter _importer;

    public ExternalImportController(IExternalInstallImporter importer)
    {
        _importer = importer;
    }

    // What importing would do, item by item.
    [HttpPost("preview")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<List<ExternalImportItemResource>> Preview([FromBody] ExternalImportRequest request)
    {
        try
        {
            var options = ExternalImportOptionsParser.Parse(request.Kind, request.Folder, request.PathFrom ?? string.Empty, request.PathTo ?? string.Empty);

            return TypedResults.Ok(_importer.Preview(options).Select(i => new ExternalImportItemResource
            {
                Name = i.Name,
                Status = i.Status.ToString().ToLowerInvariant(),
                Reason = i.Reason
            }).ToList());
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or global::System.Data.Common.DbException)
        {
            throw new BadRequestException(ex.Message);
        }
    }
}
