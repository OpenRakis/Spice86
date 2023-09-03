namespace Spice86.Core.Emulator.Function.Dump;

using System.Text.Json;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// A class that provides functionality for writing various recorded data to files.
/// </summary>
public class RecorderDataWriter : RecordedDataIoHandler {
    private readonly ILoggerService _loggerService;
    private readonly ICpuState _state;
    private readonly IMemory _memory;
    private readonly CallbackHandler _callbackHandler;
    private readonly Configuration _configuration;
    private readonly ExecutionFlowRecorder _executionFlowRecorder;
    private readonly MemoryDataExporter _memoryDataExporter;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="executionFlowRecorder">The class that records machine code execution flow.</param>
    /// <param name="dumpDirectory">Where to dump the data.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="callbackHandler">The class that stores callback instructions.</param>
    /// <param name="configuration">The emulator configuration.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public RecorderDataWriter(IMemory memory, ICpuState state, CallbackHandler callbackHandler, Configuration configuration, ExecutionFlowRecorder executionFlowRecorder, string dumpDirectory, ILoggerService loggerService) : base(dumpDirectory) {
        _loggerService = loggerService;
        _configuration = configuration;
        _executionFlowRecorder = executionFlowRecorder;
        _state = state;
        _memory = memory;
        _callbackHandler = callbackHandler;
        _memoryDataExporter = new(memory, callbackHandler, configuration, dumpDirectory, loggerService);
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
    private void DumpGhidraSymbols(ExecutionFlowRecorder executionFlowRecorder, FunctionHandler functionHandler) {
        new GhidraSymbolsDumper(_loggerService).Dump(executionFlowRecorder, functionHandler, SymbolsFile);
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
    private void DumpExecutionFlow() => new ExecutionFlowDumper(_loggerService).Dump(_executionFlowRecorder, ExecutionFlowFile);
}