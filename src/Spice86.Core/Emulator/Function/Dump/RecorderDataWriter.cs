namespace Spice86.Core.Emulator.Function.Dump;

using System.Text.Json;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Interfaces;

/// <summary>
/// A class that provides functionality for writing various recorded data to files.
/// </summary>
public class RecorderDataWriter : RecordedDataIoHandler {
    private readonly ILoggerService _loggerService;
    private readonly State _state;
    private readonly ExecutionFlowDumper _executionFlowDumper;
    private readonly ExecutionFlowRecorder _executionFlowRecorder;
    private readonly MemoryDataExporter _memoryDataExporter;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="executionFlowRecorder">The class that records machine code execution flow.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="memoryDataExporter">The class used to export memory contents to a file.</param>
    /// <param name="executionFlowDumper">The class that reads and dumps execution flow to a file.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="dumpDirectory">Where to dump the data.</param>
    public RecorderDataWriter(ExecutionFlowRecorder executionFlowRecorder, State state, MemoryDataExporter memoryDataExporter, ExecutionFlowDumper executionFlowDumper, ILoggerService loggerService, string dumpDirectory) : base(dumpDirectory) {
        _loggerService = loggerService;
        _executionFlowRecorder = executionFlowRecorder;
        _state = state;
        _memoryDataExporter = memoryDataExporter;
        _executionFlowDumper = executionFlowDumper;
    }

    /// <summary>
    /// Dumps all recorded data to their respective files.
    /// </summary>
    public void DumpAll(ExecutionFlowRecorder executionFlowRecorder, FunctionHandler functionHandler) {
        _loggerService.Verbose("Dumping all data to {DumpDirectory}", DumpDirectory);
        DumpCpuRegisters("");
        DumpMemory("");
        DumpGhidraSymbols(executionFlowRecorder, functionHandler);
        DumpExecutionFlow();
    }

    /// <summary>
    /// Dumps the Ghidra symbols to the file system.
    /// </summary>
    private void DumpGhidraSymbols(ExecutionFlowRecorder executionFlowRecorder, FunctionHandler functionHandler) =>
        new GhidraSymbolsDumper(_loggerService).Dump(executionFlowRecorder, functionHandler, SymbolsFile);

    /// <summary>
    /// Dumps the CPU registers to the file system.
    /// </summary>
    /// <param name="suffix">The suffix to add to the file name.</param>
    public void DumpCpuRegisters(string suffix) {
        string path = GenerateDumpFileName($"CpuRegisters{suffix}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(_state));
    }

    /// <summary>
    /// Dumps the machine's memory to the file system.
    /// </summary>
    /// <param name="suffix">The suffix to add to the file name.</param>
    public void DumpMemory(string suffix) => _memoryDataExporter.DumpMemory(suffix);

    /// <summary>
    /// Dumps the execution flow data to the file system.
    /// </summary>
    private void DumpExecutionFlow() => _executionFlowDumper.Dump(_executionFlowRecorder, ExecutionFlowFile);
}