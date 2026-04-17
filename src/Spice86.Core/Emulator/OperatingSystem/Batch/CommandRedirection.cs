namespace Spice86.Core.Emulator.OperatingSystem.Batch;

internal readonly struct CommandRedirection {
    internal CommandRedirection(string inputPath, string outputPath, bool appendOutput, string errorPath, bool appendError) {
        InputPath = inputPath;
        OutputPath = outputPath;
        AppendOutput = appendOutput;
        ErrorPath = errorPath;
        AppendError = appendError;
    }

    internal string InputPath { get; }
    internal string OutputPath { get; }
    internal bool AppendOutput { get; }
    internal string ErrorPath { get; }
    internal bool AppendError { get; }
    internal bool HasAny => !string.IsNullOrWhiteSpace(InputPath) || !string.IsNullOrWhiteSpace(OutputPath) || !string.IsNullOrWhiteSpace(ErrorPath);
}