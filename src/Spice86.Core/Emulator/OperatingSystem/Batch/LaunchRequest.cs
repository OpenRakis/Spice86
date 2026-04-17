namespace Spice86.Core.Emulator.OperatingSystem.Batch;

internal abstract class LaunchRequest {
    protected LaunchRequest(CommandRedirection redirection) {
        Redirection = redirection;
    }

    internal CommandRedirection Redirection { get; }
    internal abstract LaunchRequest WithRedirection(CommandRedirection redirection);
}