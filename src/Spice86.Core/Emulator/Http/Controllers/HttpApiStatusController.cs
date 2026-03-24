namespace Spice86.Core.Emulator.Http.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.Http.Contracts;
using Spice86.Core.Emulator.VM;

using System.Threading;

/// <summary>
/// CPU and emulator status endpoints.
/// </summary>
[ApiController]
[Route("api/status")]
public sealed class HttpApiStatusController : HttpApiControllerBase {
    /// <summary>Operation ID for the <see cref="GetStatus"/> endpoint.</summary>
    public const string GetStatusOperationId = "HttpApiStatusController.GetStatus";

    /// <summary>Operation ID for the <see cref="Pause"/> endpoint.</summary>
    public const string PauseOperationId = "HttpApiStatusController.Pause";

    /// <summary>Operation ID for the <see cref="Unpause"/> endpoint.</summary>
    public const string UnpauseOperationId = "HttpApiStatusController.Unpause";

    /// <summary>Initializes a new instance of <see cref="HttpApiStatusController"/>.</summary>
    /// <param name="httpApiState">Shared emulator state injected by the DI container.</param>
    /// <param name="pauseHandler">Pause handler used by HTTP operations.</param>
    /// <param name="logger">Logger instance.</param>
    public HttpApiStatusController(HttpApiState httpApiState, IPauseHandler pauseHandler,
        ILogger<HttpApiStatusController> logger) : base(httpApiState, pauseHandler, logger) {
    }

    /// <summary>Returns a snapshot of the current CPU and emulator state.</summary>
    /// <param name="cancellationToken">Cancellation token for the current HTTP request.</param>
    /// <returns>200 OK with <see cref="HttpApiStatusResponse"/>.</returns>
    [HttpGet]
    public ActionResult<HttpApiStatusResponse> GetStatus(CancellationToken cancellationToken) {
        bool isPaused = PauseHandler.IsPaused;
        return WhilePaused("GET /api/status", cancellationToken,
            () => CreateStatusResponse(isPaused));
    }

    /// <summary>Pauses the emulator.</summary>
    /// <param name="cancellationToken">Cancellation token for the current HTTP request.</param>
    [HttpPost("pause")]
    public ActionResult<HttpApiStatusResponse> Pause(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        PauseHandler.RequestPause("HTTP POST pause");
        return Ok(CreateStatusResponse(isPaused: true));
    }

    /// <summary>Unpauses the emulator.</summary>
    /// <param name="cancellationToken">Cancellation token for the current HTTP request.</param>
    [HttpPost("unpause")]
    public ActionResult<HttpApiStatusResponse> Unpause(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        PauseHandler.Resume();
        return Ok(CreateStatusResponse(isPaused: false));
    }

    private HttpApiStatusResponse CreateStatusResponse(bool isPaused) {
        return new HttpApiStatusResponse(
            IsPaused: isPaused,
            IsCpuRunning: HttpApiState.State.IsRunning,
            Cycles: HttpApiState.State.Cycles,
            Cs: HttpApiState.State.CS,
            Ip: HttpApiState.State.IP,
            IpPhysicalAddress: HttpApiState.State.IpPhysicalAddress,
            MemorySizeBytes: HttpApiState.Memory.Length);
    }
}
