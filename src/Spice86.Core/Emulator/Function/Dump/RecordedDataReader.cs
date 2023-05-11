namespace Spice86.Core.Emulator.Function.Dump;

using Spice86.Shared.Interfaces;

using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.Memory;

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
    /// <param name="recordData">A value indicating whether the execution flow recorder should record data.</param>
    /// <returns>The execution flow recorder read from the file, or a new instance if the file does not exist.</returns>
    public ExecutionFlowRecorder ReadExecutionFlowRecorderFromFileOrCreate(bool recordData) {
        ExecutionFlowRecorder executionFlowRecorder =
            new ExecutionFlowDumper(
                    _loggerService)
                .ReadFromFileOrCreate(ExecutionFlowFile);
        executionFlowRecorder.RecordData = recordData;
        return executionFlowRecorder;
    }

    /// <summary>
    /// Reads the Ghidra symbols data from a file or creates a new one if the file does not exist.
    /// </summary>
    /// <returns>A dictionary of segmented addresses and their corresponding function information, read from the file or a new instance if the file does not exist.</returns>
    public IDictionary<SegmentedAddress, FunctionInformation> ReadGhidraSymbolsFromFileOrCreate() {
        return new GhidraSymbolsDumper(_loggerService).ReadFromFileOrCreate(SymbolsFile);
    }
}
