namespace Spice86.Core.Emulator.Function.Dump;

/// <summary>
/// An abstract class that provides a base implementation for recording execution data.
/// </summary>
public abstract class RecordedDataIoHandler {
    /// <summary>
    /// The directory where data dumps will be saved.
    /// </summary>
    public string DumpDirectory { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordedDataIoHandler"/> class with the specified dump directory.
    /// </summary>
    /// <param name="dumpDirectory">The directory where data dumps will be saved.</param>
    public RecordedDataIoHandler(string dumpDirectory) {
        DumpDirectory = dumpDirectory;
    }

    /// <summary>
    /// Gets the file name of the execution flow dump.
    /// </summary>
    protected string ExecutionFlowFile => GenerateDumpFileName("ExecutionFlow.json");

    /// <summary>
    /// Gets the file name of the Ghidra symbols dump.
    /// </summary>
    protected string SymbolsFile => GenerateDumpFileName("GhidraSymbols.txt");

    /// <summary>
    /// Generates a dump file name with the specified suffix.
    /// </summary>
    /// <param name="suffix">The suffix to add to the dump file name.</param>
    /// <returns>A dump file name with the specified suffix.</returns>
    protected string GenerateDumpFileName(string suffix) {
        return $"{DumpDirectory}/spice86dump{suffix}";
    }
}
