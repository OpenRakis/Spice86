namespace Spice86.Core.Emulator.OperatingSystem.Batch;

internal readonly struct CommandExecutionContext {
    internal CommandExecutionContext(string preprocessedLine, string argumentPart,
        string resolvedCommandToken, CommandRedirection redirection) {
        PreprocessedLine = preprocessedLine;
        ArgumentPart = argumentPart;
        ResolvedCommandToken = resolvedCommandToken;
        Redirection = redirection;
    }

    internal string PreprocessedLine { get; }
    internal string ArgumentPart { get; }
    internal string ResolvedCommandToken { get; }
    internal CommandRedirection Redirection { get; }
}