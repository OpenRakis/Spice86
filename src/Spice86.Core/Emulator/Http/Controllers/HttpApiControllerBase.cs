namespace Spice86.Core.Emulator.Http.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.VM;

using System.Net;

/// <summary>
/// Base class for HTTP API controllers that need pause-safe emulator access.
/// </summary>
public abstract class HttpApiControllerBase : ControllerBase {
    private readonly ILogger _logger;

    /// <summary>Shared emulator state.</summary>
    protected HttpApiState HttpApiState { get; }

    /// <summary>Pause handler for pausing/resuming the emulator.</summary>
    protected IPauseHandler PauseHandler { get; }

    /// <summary>Initializes shared dependencies.</summary>
    protected HttpApiControllerBase(HttpApiState httpApiState, IPauseHandler pauseHandler, ILogger logger) {
        HttpApiState = httpApiState;
        PauseHandler = pauseHandler;
        _logger = logger;
    }

    /// <summary>
    /// Temporarily pauses the emulator (if not already paused), executes <paramref name="action"/>,
    /// and resumes afterward. Handles cancellation and unexpected exceptions uniformly.
    /// </summary>
    protected ActionResult<T> WhilePaused<T>(string operation, CancellationToken cancellationToken, Func<T> action) {
        cancellationToken.ThrowIfCancellationRequested();
        bool wasPaused = PauseHandler.IsPaused;
        if (!wasPaused) {
            PauseHandler.RequestPause(operation);
        }

        try {
            cancellationToken.ThrowIfCancellationRequested();
            return Ok(action());
        } catch (Exception ex) when (ex is not HttpApiException and not OperationCanceledException) {
            _logger.LogError(ex, "{Operation} failed", operation);
            throw new HttpApiException(HttpStatusCode.InternalServerError,
                $"{operation}: {ex.GetType().Name}: {ex.Message}");
        } finally {
            if (!wasPaused) {
                PauseHandler.Resume();
            }
        }
    }
}
