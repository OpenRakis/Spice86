namespace Spice86.Core.Emulator.Http.Controllers;

using Microsoft.AspNetCore.Mvc;

using Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// CPU and emulator status endpoints.
/// </summary>
[ApiController]
[Route("api/status")]
public sealed class HttpApiStatusController : ControllerBase {
    private readonly HttpApiState _httpApiState;

    /// <summary>Initializes a new instance of <see cref="HttpApiStatusController"/>.</summary>
    /// <param name="httpApiState">Shared emulator state injected by the DI container.</param>
    public HttpApiStatusController(HttpApiState httpApiState) {
        _httpApiState = httpApiState;
    }

    /// <summary>Returns a snapshot of the current CPU and emulator state.</summary>
    /// <returns>200 OK with <see cref="HttpApiStatusResponse"/>.</returns>
    [HttpGet]
    public ActionResult<HttpApiStatusResponse> GetStatus() {
        HttpApiStatusResponse response = new(
            IsPaused: _httpApiState.PauseHandler.IsPaused,
            IsCpuRunning: _httpApiState.State.IsRunning,
            Cycles: _httpApiState.State.Cycles,
            Cs: _httpApiState.State.CS,
            Ip: _httpApiState.State.IP,
            IpPhysicalAddress: _httpApiState.State.IpPhysicalAddress,
            MemorySizeBytes: _httpApiState.Memory.Length);
        return Ok(response);
    }
}
