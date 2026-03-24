namespace Spice86.Core.Emulator.Http.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using Spice86.Core.Emulator.Http.Contracts;

using System.Linq;
using System.Threading;

/// <summary>
/// API root endpoint.
/// </summary>
[ApiController]
[Route("api")]
public sealed class HttpApiController : ControllerBase {
    private readonly EndpointDataSource _endpointDataSource;

    /// <summary>Initializes a new instance of <see cref="HttpApiController"/>.</summary>
    /// <param name="endpointDataSource">ASP.NET Core composite endpoint data source used to enumerate registered routes.</param>
    public HttpApiController(EndpointDataSource endpointDataSource) {
        _endpointDataSource = endpointDataSource;
    }

    /// <summary>Returns API metadata and the list of available endpoints.</summary>
    /// <param name="cancellationToken">Cancellation token for the current HTTP request.</param>
    /// <returns>200 OK with <see cref="HttpApiInfoResponse"/>.</returns>
    [HttpGet]
    public ActionResult<HttpApiInfoResponse> GetInfo(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<string> endpoints = [.. _endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .Where(path => path is not null && path.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            .Select(path => $"/{path}")
            .Distinct()
            .Order()];

        return Ok(new HttpApiInfoResponse(
            Name: "Spice86 HTTP API",
            Version: "v1",
            Endpoints: endpoints));
    }

}
