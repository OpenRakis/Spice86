namespace Spice86.Core.Emulator;

using Function.Dump;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Devices.Timer;
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

/// <summary>
/// The class that is responsible for executing a program in the emulator. Only supports COM, EXE, and BIOS files.
/// </summary>
public sealed class ProgramExecutor : IDisposable {
    private bool _disposed;
    private readonly ILoggerService _loggerService;
    private readonly IPauseHandler _pauseHandler;
    private readonly Configuration _configuration;
    private readonly GdbServer? _gdbServer;
    private readonly EmulationLoop _emulationLoop;
    private readonly EmulatorStateSerializer _emulatorStateSerializer;

    /// <summary>
    /// Initializes a new instance of <see cref="ProgramExecutor"/>
    /// </summary>
    /// <param name="configuration">The emulator <see cref="Configuration"/> to use.</param>
    /// <param name="emulatorBreakpointsManager">The class that manages machine code execution breakpoints.</param>
    /// <param name="emulatorStateSerializer">The class that is responsible for serializing the state of the emulator to a directory.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="cpu">The emulated x86 CPU.</param>
    /// <param name="cfgCpu">The emulated x86 CPU, CFG version.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="timer">The programmable interval timer.</param>
    /// <param name="dos">The DOS kernel.</param>
    /// <param name="callbackHandler">The class that stores callback instructions definitions.</param>
    /// <param name="functionHandler">The class that handles functions calls for the emulator.</param>
    /// <param name="executionFlowRecorder">The class that records machine code execution flow.</param>
    /// <param name="pauseHandler">The object responsible for pausing an resuming the emulation.</param>
    /// <param name="screenPresenter">The user interface class that displays video output in a dedicated thread.</param>
    /// <param name="loggerService">The logging service to use.</param>
    public ProgramExecutor(Configuration configuration,
        EmulatorBreakpointsManager emulatorBreakpointsManager, EmulatorStateSerializer emulatorStateSerializer,
        IMemory memory, Cpu cpu, CfgCpu cfgCpu, State state, Timer timer, Dos dos,
        CallbackHandler callbackHandler, FunctionHandler functionHandler,
        ExecutionFlowRecorder executionFlowRecorder, IPauseHandler pauseHandler, IScreenPresenter? screenPresenter, ILoggerService loggerService) {
        _configuration = configuration;
        _loggerService = loggerService;
        _emulatorStateSerializer = emulatorStateSerializer;
        _pauseHandler = pauseHandler;
        _emulationLoop = new EmulationLoop(_loggerService, functionHandler, cfgCpu, state, timer,
            emulatorBreakpointsManager, pauseHandler);
        if (configuration.GdbPort.HasValue) {
            _gdbServer = CreateGdbServer(configuration, memory, cpu, state, callbackHandler, functionHandler,
                executionFlowRecorder, emulatorBreakpointsManager, pauseHandler, _loggerService);
        }
        ExecutableFileLoader loader = CreateExecutableFileLoader(configuration, memory, state, dos.EnvironmentVariables, dos.FileManager, dos.MemoryManager);
        if (configuration.InitializeDOS is null) {
            configuration.InitializeDOS = loader.DosInitializationNeeded;
            if (loggerService.IsEnabled(LogEventLevel.Verbose)) {
                loggerService.Verbose("InitializeDOS parameter not provided. Guessed value is: {InitializeDOS}", configuration.InitializeDOS);
            }
        }

        if (screenPresenter is not null) {
            screenPresenter.UserInterfaceInitialized += Run;
        }
        LoadFileToRun(configuration, loader);
    }

    /// <summary>
    /// Starts the loaded program.
    /// </summary>
    public void Run() {
        if (_gdbServer is not null) {
            _gdbServer?.StartServerAndWait();
        } else if (_configuration.Debug) {
            _pauseHandler.RequestPause("Starting the emulated program paused was requested");
        }
        _emulationLoop.Run();

        if (_configuration.DumpDataOnExit is not false) {
            _emulatorStateSerializer.SerializeEmulatorStateToDirectory(_configuration.RecordedDataDirectory);
        }
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
    
    private static GdbServer? CreateGdbServer(Configuration configuration, IMemory memory, Cpu cpu, State state, CallbackHandler callbackHandler, FunctionHandler functionHandler,
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

    /// <inheritdoc cref="IDisposable" />
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
            }
            _disposed = true;
        }
    }
}