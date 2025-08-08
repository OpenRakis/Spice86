namespace Spice86.Core.Emulator.Function.Dump;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Interfaces;

using System.Text.Json;

/// <summary>
/// A class that provides functionality for writing various recorded data to files.
/// </summary>
public class RecordedDataWriter : RecordedDataIoHandler {
    private readonly ILoggerService _loggerService;
    private readonly State _state;
    private readonly IExecutionDumpFactory _executionDumpFactory;
    private readonly MemoryDataExporter _memoryDataExporter;
    private readonly FunctionCatalogue _functionCatalogue;
    
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="executionDumpFactory">The class that dumps machine code execution flow.</param>
    /// <param name="memoryDataExporter">The class used to dump main memory data properly.</param>
    /// <param name="functionCatalogue">The list of functions encountered.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="dumpDirectory">Where to dump the data.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public RecordedDataWriter(State state,
        IExecutionDumpFactory executionDumpFactory,
        MemoryDataExporter memoryDataExporter, 
        FunctionCatalogue functionCatalogue,
        string dumpDirectory, ILoggerService loggerService) : base(dumpDirectory) {
        _loggerService = loggerService;
        _executionDumpFactory = executionDumpFactory;
        _state = state;
        _memoryDataExporter = memoryDataExporter;
        _functionCatalogue = functionCatalogue;
    }

    /// <summary>
    /// Dumps all recorded data to their respective files.
    /// </summary>
    public void DumpAll() {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Dumping all data to {DumpDirectory}", DumpDirectory);
        }
        DumpCpuRegisters("");
        DumpMemory("");
        ExecutionDump executionDump = _executionDumpFactory.Dump();
        DumpGhidraSymbols(executionDump);
        DumpExecutionFlow(executionDump);
    }

    /// <summary>
    /// Dumps the Ghidra symbols to the file system.
    /// </summary>
    private void DumpGhidraSymbols(ExecutionDump executionDump) {
        new GhidraSymbolsDumper(_loggerService).Dump(executionDump, _functionCatalogue, SymbolsFile);
    }

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
    public void DumpMemory(string suffix) {
        _memoryDataExporter.DumpMemory(suffix);
    }

    /// <summary>
    /// Dumps the execution flow data to the file system.
    /// </summary>
    private void DumpExecutionFlow(ExecutionDump executionDump) => new ExecutionFlowDumper(_loggerService).Dump(executionDump, ExecutionFlowFile);
}