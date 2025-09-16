namespace Spice86.Core.Emulator.Function.Dump;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.Breakpoint.Serializable;
using Spice86.Shared.Interfaces;

/// <summary>
/// This class is responsible for serializing the emulator state to a directory.
/// </summary>
public class EmulatorStateSerializer {
    private readonly State _state;
    private readonly IExecutionDumpFactory _executionDumpFactory;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly ILoggerService _loggerService;
    private readonly MemoryDataExporter _memoryDataExporter;
    private readonly EmulatorBreakpointsSerializer _emulatorBreakpointsSerializer;
    
    /// <summary>
    /// Initializes a new instance of <see cref="EmulatorStateSerializer"/>.
    /// </summary>
    public EmulatorStateSerializer(
        MemoryDataExporter memoryDataExporter, 
        State state,
        IExecutionDumpFactory executionDumpFactory,
        EmulatorBreakpointsSerializer emulatorBreakpointsSerializer,
        FunctionCatalogue functionCatalogue, 
        ILoggerService loggerService) {
        
        _state = state;
        _memoryDataExporter = memoryDataExporter;
        _executionDumpFactory = executionDumpFactory;
        _functionCatalogue = functionCatalogue;
        _loggerService = loggerService;
        _emulatorBreakpointsSerializer = emulatorBreakpointsSerializer;
    }
    
    /// <summary>
    /// Dumps the emulator state to the specified directory.
    /// </summary>
    public void SerializeEmulatorStateToDirectory(string dirPath) {
        new RecordedDataWriter(
                _state,
                _executionDumpFactory,
                _memoryDataExporter,
                _functionCatalogue,
                dirPath, 
                _loggerService)
            .DumpAll();
        _emulatorBreakpointsSerializer.SaveBreakpoints(dirPath);
    }
}