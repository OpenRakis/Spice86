namespace Spice86.Core.Emulator.Function.Dump;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// This class is responsible for serializing the emulator state to a directory.
/// </summary>
public class EmulatorStateSerializer {
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly CallbackHandler _callbackHandler;
    private readonly Configuration _configuration;
    private readonly ExecutionFlowRecorder _executionFlowRecorder;
    private readonly FunctionHandler _functionHandler;
    private readonly ILoggerService _loggerService;
    
    /// <summary>
    /// Initializes a new instance of <see cref="EmulatorStateSerializer"/>.
    /// </summary>
    public EmulatorStateSerializer(Configuration configuration, IMemory memory, State state, CallbackHandler callbackHandler, ExecutionFlowRecorder executionFlowRecorder, FunctionHandler cpuFunctionHandler, ILoggerService loggerService) {
        _configuration = configuration;
        _memory = memory;
        _state = state;
        _callbackHandler = callbackHandler;
        _executionFlowRecorder = executionFlowRecorder;
        _functionHandler = cpuFunctionHandler;
        _loggerService = loggerService;
    }
    
    
    /// <summary>
    /// Dumps the emulator state to the specified directory.
    /// </summary>
    /// <param name="path">The directory used for dumping the emulator state.</param>
    public void SerializeEmulatorStateToDirectory(string path) {
        new RecorderDataWriter(_memory,
                _state,
                _callbackHandler,
                _configuration,
                _executionFlowRecorder,
                path, _loggerService)
            .DumpAll(_executionFlowRecorder, _functionHandler);
    }
}