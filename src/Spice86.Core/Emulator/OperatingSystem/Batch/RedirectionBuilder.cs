namespace Spice86.Core.Emulator.OperatingSystem.Batch;

internal sealed class RedirectionBuilder {
    private string _inputPath = string.Empty;
    private string _outputPath = string.Empty;
    private bool _appendOutput;
    private string _errorPath = string.Empty;
    private bool _appendError;

    internal void SetInput(string path) {
        _inputPath = path;
    }

    internal void SetOutput(string path, bool append) {
        _outputPath = path;
        _appendOutput = append;
    }

    internal void SetError(string path, bool append) {
        _errorPath = path;
        _appendError = append;
    }

    internal CommandRedirection Build() {
        return new CommandRedirection(_inputPath, _outputPath, _appendOutput, _errorPath, _appendError);
    }
}