namespace Spice86.Core.Emulator.OperatingSystem.Batch;

internal sealed class InternalProgramLaunchRequest : LaunchRequest {
    internal InternalProgramLaunchRequest(byte[] comProgramBytes, CommandRedirection redirection)
        : base(redirection) {
        ComProgramBytes = comProgramBytes;
    }

    internal byte[] ComProgramBytes { get; }

    internal override LaunchRequest WithRedirection(CommandRedirection redirection) =>
        new InternalProgramLaunchRequest(ComProgramBytes, redirection);
}