namespace Spice86.Core.Emulator.OperatingSystem.Batch;

/// <summary>
/// Represents a batch command outcome that keeps execution inside the current batch engine flow.
/// </summary>
internal sealed class ContinueBatchExecutionLaunchRequest : LaunchRequest {
    /// <summary>
    /// Shared instance used when command execution continues without launching a separate program.
    /// </summary>
    internal static readonly ContinueBatchExecutionLaunchRequest Instance = new();

    private ContinueBatchExecutionLaunchRequest() : base(default) {
    }

    internal override LaunchRequest WithRedirection(CommandRedirection redirection) => this;
}