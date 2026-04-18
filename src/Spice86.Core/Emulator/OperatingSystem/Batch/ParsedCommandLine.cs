namespace Spice86.Core.Emulator.OperatingSystem.Batch;

internal readonly struct ParsedCommandLine {
    internal ParsedCommandLine(string commandLineWithoutRedirection, CommandRedirection redirection) {
        CommandLineWithoutRedirection = commandLineWithoutRedirection;
        Redirection = redirection;
    }

    internal string CommandLineWithoutRedirection { get; }
    internal CommandRedirection Redirection { get; }
}