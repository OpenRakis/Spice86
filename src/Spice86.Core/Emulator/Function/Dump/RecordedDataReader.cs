namespace Spice86.Core.Emulator.Function.Dump;

using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// A class that provides methods for reading recorded data from files.
/// </summary>
public class RecordedDataReader : RecordedDataIoHandler {
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordedDataReader"/> class.
    /// </summary>
    /// <param name="dumpDirectory">The directory where data dumps will be saved.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public RecordedDataReader(string dumpDirectory, ILoggerService loggerService) : base(dumpDirectory) {
        _loggerService = loggerService;
    }

    /// <summary>
    /// Reads the execution flow recorder data from a file or creates a new one if the file does not exist.
    /// </summary>
    /// <returns>The execution dump read from the file, or a new instance if the file does not exist.</returns>
    public ExecutionDump ReadExecutionDumpFromFileOrCreate() {
        return new ExecutionFlowDumper(_loggerService)
             .ReadFromFileOrCreate(ExecutionFlowFile);
    }

    /// <summary>
    /// Reads the Ghidra symbols data from a file or creates a new one if the file does not exist.
    /// </summary>
    /// <returns>A list of function information, read from the file or a new instance if the file does not exist.</returns>
    public IEnumerable<FunctionInformation> ReadGhidraSymbolsFromFileOrCreate() {
        return new GhidraSymbolsDumper(_loggerService).ReadFromFileOrCreate(SymbolsFile);
    }
}