namespace Spice86.Core.Emulator.OperatingSystem.Batch;

internal sealed class BootLaunchRequest : LaunchRequest {
    internal BootLaunchRequest(char driveLetter, CommandRedirection redirection)
        : base(redirection) {
        DriveLetter = driveLetter;
    }

    internal char DriveLetter { get; }

    internal override LaunchRequest WithRedirection(CommandRedirection redirection) =>
        new BootLaunchRequest(DriveLetter, redirection);
}