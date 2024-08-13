namespace Spice86.Core.Emulator;

using Function.Dump;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.LoadableFile.Bios;
using Spice86.Core.Emulator.LoadableFile.Dos.Com;
using Spice86.Core.Emulator.LoadableFile.Dos.Exe;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Security.Cryptography;

using GeneralRegisters = Spice86.Core.Emulator.CPU.Registers.GeneralRegisters;

/// <inheritdoc cref="IProgramExecutor"/>
public sealed class ProgramExecutor : IProgramExecutor {
    private bool _disposed;
    private readonly ILoggerService _loggerService;
    private readonly Dos _dos;
    private readonly Configuration _configuration;
    private readonly GdbServer? _gdbServer;
    private readonly EmulationLoop _emulationLoop;
    private readonly CallbackHandler _callbackHandler;
    private readonly FunctionHandler _functionHandler;
    private readonly ExecutionFlowRecorder _executionFlowRecorder;
    private readonly IPauseHandler _pauseHandler;
    private readonly IMemory _memory;
    private readonly State _cpuState;

    /// <summary>
    /// Initializes a new instance of <see cref="ProgramExecutor"/>
    /// </summary>
    /// <param name="configuration">The emulator <see cref="Configuration"/> to use.</param>
    /// <param name="loggerService">The logging service to use. Provided via DI.</param>
    /// <param name="emulatorBreakpointsManager">The class that manages machine code execution breakpoints.</param>
    /// <param name="machine">The dumb service container that centralizes emulator devices.</param>
    /// <param name="dos">The DOS kernel.</param>
    /// <param name="callbackHandler">The class that stores callback instructions definitions.</param>
    /// <param name="functionHandler">The class that handles functions calls for the emulator.</param>
    /// <param name="executionFlowRecorder">The class that records machine code execution flow.</param>
    /// <param name="pauseHandler">The object responsible for pausing an resuming the emulation.</param>
    public ProgramExecutor(Configuration configuration, ILoggerService loggerService,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        Machine machine, Dos dos,
        CallbackHandler callbackHandler, FunctionHandler functionHandler,
        ExecutionFlowRecorder executionFlowRecorder, IPauseHandler pauseHandler) {
        _configuration = configuration;
        _loggerService = loggerService;
        _dos = dos;
        _pauseHandler = pauseHandler;
        Machine = machine;
        _memory = Machine.Memory;
        _cpuState = Machine.CpuState;
        _callbackHandler = callbackHandler;
        _functionHandler = functionHandler;
        _executionFlowRecorder = executionFlowRecorder;
        _emulationLoop = new(_loggerService, _functionHandler, Machine.Cpu, _cpuState, Machine.Timer,
            emulatorBreakpointsManager, Machine.DmaController, pauseHandler);
        if (configuration.GdbPort.HasValue) {
            _gdbServer = CreateGdbServer(configuration, _memory, Machine.Cpu, _cpuState, _callbackHandler, _functionHandler,
                _executionFlowRecorder, emulatorBreakpointsManager, _pauseHandler, _loggerService);
        }
        ExecutableFileLoader loader = CreateExecutableFileLoader(configuration, _memory, _cpuState, dos.EnvironmentVariables, dos.FileManager, dos.MemoryManager);
        if (configuration.InitializeDOS is null) {
            configuration.InitializeDOS = loader.DosInitializationNeeded;
            if (loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                loggerService.Verbose("InitializeDOS parameter not provided. Guessed value is: {InitializeDOS}", configuration.InitializeDOS);
            }
        }
        LoadFileToRun(configuration, loader);
    }

    /// <summary>
    /// The emulator machine.
    /// </summary>
    public Machine Machine { get; }

    /// <inheritdoc/>
    public void Run() {
        _gdbServer?.StartServerAndWait();
        _emulationLoop.Run();
        if (_configuration.DumpDataOnExit is not false) {
            DumpEmulatorStateToDirectory(_configuration.RecordedDataDirectory);
        }
    }
    
    /// <summary>
    /// Steps a single instruction for the internal UI debugger
    /// </summary>
    /// <remarks>Depends on the presence of the GDBServer and GDBCommandHandler</remarks>
    public void StepInstruction() {
        _gdbServer?.StepInstruction();
        _pauseHandler.Resume();
    }

    /// <inheritdoc/>
    public void DumpEmulatorStateToDirectory(string path) {
        new RecorderDataWriter(_memory,
                _cpuState,
                _callbackHandler,
                _configuration,
                _executionFlowRecorder,
               path, _loggerService)
            .DumpAll(_executionFlowRecorder, _functionHandler);
    }

    private static void CheckSha256Checksum(byte[] file, byte[]? expectedHash) {
        ArgumentNullException.ThrowIfNull(expectedHash, nameof(expectedHash));
        if (expectedHash.Length == 0) {
            // No hash check
            return;
        }

        byte[] actualHash = SHA256.HashData(file);

        if (!actualHash.AsSpan().SequenceEqual(expectedHash)) {
            string error =
                $"File does not match the expected SHA256 checksum, cannot execute it.\nExpected checksum is {ConvertUtils.ByteArrayToHexString(expectedHash)}.\nGot {ConvertUtils.ByteArrayToHexString(actualHash)}\n";
            throw new UnrecoverableException(error);
        }
    }

    private ExecutableFileLoader CreateExecutableFileLoader(Configuration configuration, IMemory memory, State cpuState, EnvironmentVariables environmentVariables,
        DosFileManager fileManager, DosMemoryManager memoryManager) {
        string? executableFileName = configuration.Exe;
        ArgumentException.ThrowIfNullOrEmpty(executableFileName);

        string lowerCaseFileName = executableFileName.ToLowerInvariant();
        ushort entryPointSegment = configuration.ProgramEntryPointSegment;
        if (lowerCaseFileName.EndsWith(".exe")) {
            return new ExeLoader(memory,
                cpuState,
                _loggerService,
                environmentVariables,
                fileManager,
                memoryManager,
                entryPointSegment);
        }

        if (lowerCaseFileName.EndsWith(".com")) {
            return new ComLoader(memory,
                cpuState,
                _loggerService,
                environmentVariables,
                fileManager,
                memoryManager,
                entryPointSegment);
        }

        return new BiosLoader(memory, cpuState, _loggerService);
    }
    
    private GdbServer? CreateGdbServer(Configuration configuration, IMemory memory, Cpu cpu, State state, CallbackHandler callbackHandler, FunctionHandler functionHandler,
        ExecutionFlowRecorder executionFlowRecorder, EmulatorBreakpointsManager emulatorBreakpointsManager, IPauseHandler pauseHandler, ILoggerService loggerService) {
        if (configuration.GdbPort is null) {
            return null;
        }
        return new GdbServer(configuration, memory, cpu, state, callbackHandler, functionHandler, executionFlowRecorder, emulatorBreakpointsManager, pauseHandler, loggerService);
    }

    private void LoadFileToRun(Configuration configuration, ExecutableFileLoader loader) {
        string? executableFileName = configuration.Exe;
        ArgumentException.ThrowIfNullOrEmpty(executableFileName);

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Loading file {FileName} with loader {LoaderType}", executableFileName,
                loader.GetType());
        }

        try {
            byte[] fileContent = loader.LoadFile(executableFileName, configuration.ExeArgs);
            CheckSha256Checksum(fileContent, configuration.ExpectedChecksumValue);
        } catch (IOException e) {
            throw new UnrecoverableException($"Failed to read file {executableFileName}", e);
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _gdbServer?.Dispose();
                _emulationLoop.Exit();
                Machine.Dispose();
            }
            _disposed = true;
        }
    }
}