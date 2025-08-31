namespace Spice86.Core.Emulator;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.LoadableFile.Bios;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
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
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly EmulatorStateSerializer _emulatorStateSerializer;

    /// <summary>
    /// Initializes a new instance of <see cref="ProgramExecutor"/>
    /// </summary>
    /// <param name="configuration">The emulator <see cref="Configuration"/> to use.</param>
    /// <param name="emulationLoop">The class that runs the core emulation process.</param>
    /// <param name="emulatorBreakpointsManager">The class that manages machine code execution breakpoints.</param>
    /// <param name="emulatorStateSerializer">The class that is responsible for serializing the state of the emulator to a directory.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="memoryDataExporter">The class used to dump main memory data properly.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="dos">The DOS kernel.</param>
    /// <param name="functionCatalogue">List of all functions.</param>
    /// <param name="executionDumpFactory">To dump execution flow.</param>
    /// <param name="pauseHandler">The object responsible for pausing an resuming the emulation.</param>
    /// <param name="screenPresenter">The user interface class that displays video output in a dedicated thread.</param>
    /// <param name="loggerService">The logging service to use.</param>
    public ProgramExecutor(Configuration configuration,
        EmulationLoop emulationLoop,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        EmulatorStateSerializer emulatorStateSerializer,
        IMemory memory, IFunctionHandlerProvider functionHandlerProvider,
        MemoryDataExporter memoryDataExporter, State state, Dos dos,
        FunctionCatalogue functionCatalogue,
        IExecutionDumpFactory executionDumpFactory, IPauseHandler pauseHandler,
        IScreenPresenter? screenPresenter, ILoggerService loggerService) {
        _configuration = configuration;
        _emulationLoop = emulationLoop;
        _loggerService = loggerService;
        _emulatorStateSerializer = emulatorStateSerializer;
        _pauseHandler = pauseHandler;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _gdbServer = CreateGdbServer(configuration, memory, memoryDataExporter, functionHandlerProvider,
            state, functionCatalogue,
            executionDumpFactory,
            emulatorBreakpointsManager, pauseHandler, _loggerService);
        ExecutableFileLoader loader = CreateExecutableFileLoader(configuration,
            memory, state, dos);
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
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Starting the emulation loop");
        }
        if (_configuration.Debug) {
            ToggleStartOrStopBreakpoint(BreakPointType.MACHINE_START, "Starting the emulated program in paused state.");
            ToggleStartOrStopBreakpoint(BreakPointType.MACHINE_STOP, "Stopping the emulated program in paused state.");
        }
        _gdbServer?.StartServer();
        _emulationLoop.Run();

        if (_configuration.DumpDataOnExit is not false) {
            _emulatorStateSerializer.SerializeEmulatorStateToDirectory(_configuration.RecordedDataDirectory);
        }
    }

    private void ToggleStartOrStopBreakpoint(BreakPointType type, string reason) {
        BreakPoint breakPoint = new UnconditionalBreakPoint(type, (breakpoint) => { _pauseHandler.RequestPause(reason); }, false);
        _emulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
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

    private ExecutableFileLoader CreateExecutableFileLoader(
        Configuration configuration, IMemory memory, State cpuState, Dos dos) {
        string? exe = configuration.Exe;
        ArgumentException.ThrowIfNullOrEmpty(exe);

        string upperCaseExtension = Path.GetExtension(exe.ToUpperInvariant());
        return upperCaseExtension switch {
            ".EXE" or ".COM" => dos.ProcessManager,
            _ => new BiosLoader(memory, cpuState, _loggerService),
        };
    }

    private static GdbServer? CreateGdbServer(Configuration configuration, IMemory memory,
        MemoryDataExporter memoryDataExporter, IFunctionHandlerProvider functionHandlerProvider, State state,
        FunctionCatalogue functionCatalogue, IExecutionDumpFactory executionDumpFactory,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        IPauseHandler pauseHandler, ILoggerService loggerService) {
        if (configuration.GdbPort == 0) {
            if (loggerService.IsEnabled(LogEventLevel.Information)) {
                loggerService.Information("GDB port is 0, disabling GDB server.");
            }
            return null;
        }
        return new GdbServer(configuration, memory, functionHandlerProvider, state, memoryDataExporter,
                functionCatalogue, executionDumpFactory,
            emulatorBreakpointsManager, pauseHandler, loggerService);
    }

    private void LoadFileToRun(Configuration configuration, ExecutableFileLoader loader) {
        string? executableFileName = configuration.Exe;
        ArgumentException.ThrowIfNullOrEmpty(executableFileName);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
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