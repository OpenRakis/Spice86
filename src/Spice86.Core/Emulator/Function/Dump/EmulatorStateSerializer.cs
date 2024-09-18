namespace Spice86.Core.Emulator.Function.Dump;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// This class is responsible for serializing the emulator state to a directory.
/// </summary>
public class EmulatorStateSerializer {
    private readonly State _state;
    private readonly ExecutionFlowRecorder _executionFlowRecorder;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly ILoggerService _loggerService;

    private readonly MemoryDataExporter _memoryDataExporter;
    
    /// <summary>
    /// Initializes a new instance of <see cref="EmulatorStateSerializer"/>.
    /// </summary>
    public EmulatorStateSerializer(MemoryDataExporter memoryDataExporter, State state,
        ExecutionFlowRecorder executionFlowRecorder, FunctionCatalogue functionCatalogue, ILoggerService loggerService) {
        _state = state;
        _memoryDataExporter = memoryDataExporter;
        _executionFlowRecorder = executionFlowRecorder;
        _functionCatalogue = functionCatalogue;
        _loggerService = loggerService;
    }
    
    
    /// <summary>
    /// Dumps the emulator state to the specified directory.
    /// </summary>
    /// <param name="path">The directory used for dumping the emulator state.</param>
    public void SerializeEmulatorStateToDirectory(string path) {
        new RecorderDataWriter(
                _state,
                _executionFlowRecorder,
                _memoryDataExporter,
                path, _loggerService)
            .DumpAll(_executionFlowRecorder, _functionCatalogue);
    }
}