namespace Spice86.ViewModels.Services;

/// <summary>
/// Runs the manual emulator-state dump off the UI thread and remembers the dump in flight so closing
/// the main window can wait for it.
/// </summary>
public sealed class ManualDumpRunner {
    /// <summary>The dump currently running, or a completed task when no dump has run.</summary>
    public Task InFlight { get; private set; } = Task.CompletedTask;

    /// <summary>Starts <paramref name="dump"/> on a thread-pool thread and records it as in flight.</summary>
    /// <param name="dump">The dump action to run.</param>
    /// <returns>The task of the running dump.</returns>
    public Task RunAsync(Action dump) {
        Task run = Task.Run(dump);
        InFlight = Task.WhenAll(InFlight, run);
        return run;
    }

    /// <summary>Waits for every dump started so far; a dump failure is already reported by the dump
    /// command, so the wait never throws.</summary>
    public async Task WaitAsync() {
        await InFlight.ConfigureAwait(
            ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
