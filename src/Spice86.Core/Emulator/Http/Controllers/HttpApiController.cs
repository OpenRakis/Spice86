namespace Spice86.Core.Emulator.Http.Controllers;

using Microsoft.AspNetCore.Mvc;

using Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// API root endpoint.
/// </summary>
[ApiController]
[Route("api")]
public sealed class HttpApiController : ControllerBase {
    /// <summary>Returns API metadata and the list of available endpoints.</summary>
    /// <returns>200 OK with <see cref="HttpApiInfoResponse"/>.</returns>
    [HttpGet]
    public ActionResult<HttpApiInfoResponse> GetInfo() {
        HttpApiInfoResponse response = new(
            Name: "Spice86 HTTP API",
            Version: "v1",
            Endpoints: [
                "/api/status",
                "/api/memory/{address}/byte",
                "/api/memory/{address}/range/{length}"
            ]);
        return Ok(response);
    }
}
