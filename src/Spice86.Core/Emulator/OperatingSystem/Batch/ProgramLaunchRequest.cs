namespace Spice86.Core.Emulator.OperatingSystem.Batch;

internal sealed class ProgramLaunchRequest : LaunchRequest {
    internal ProgramLaunchRequest(string programName, string commandTail, CommandRedirection redirection)
        : base(redirection) {
        ProgramName = programName;
        CommandTail = commandTail;
    }

    internal string ProgramName { get; }
    internal string CommandTail { get; }

    internal override LaunchRequest WithRedirection(CommandRedirection redirection) =>
        new ProgramLaunchRequest(ProgramName, CommandTail, redirection);
}