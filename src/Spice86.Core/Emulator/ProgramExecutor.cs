namespace Spice86.Core.Emulator;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.LoadableFile.Bios;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.IO;
using System.Security.Cryptography;
using Spice86.Core.Emulator.InterruptHandlers.Dos;

/// <summary>
/// The class that is responsible for executing a program in the emulator. Supports COM, EXE, and BIOS files.
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
    private readonly DumpFolderMetadata _dumpContext;
    public event EventHandler? EmulationStopped;

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
    /// <param name="int21Handler">The central DOS interrupt handler, used to load DOS programs.</param>
    /// <param name="functionCatalogue">List of all functions.</param>
    /// <param name="executionDumpFactory">To dump execution flow.</param>
    /// <param name="pauseHandler">The object responsible for pausing an resuming the emulation.</param>
    /// <param name="screenPresenter">The user interface class that displays video output in a dedicated thread.</param>
    /// <param name="dumpContext">The context containing program hash and dump directory information.</param>
    /// <param name="loggerService">The logging service to use.</param>
    public ProgramExecutor(Configuration configuration,
        EmulationLoop emulationLoop,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        EmulatorStateSerializer emulatorStateSerializer,
        IMemory memory, IFunctionHandlerProvider functionHandlerProvider,
        MemoryDataExporter memoryDataExporter, State state, DosInt21Handler int21Handler,
        FunctionCatalogue functionCatalogue,
        IExecutionDumpFactory executionDumpFactory, IPauseHandler pauseHandler,
        IGuiVideoPresentation? screenPresenter, DumpFolderMetadata dumpContext, ILoggerService loggerService) {
        _configuration = configuration;
        _emulationLoop = emulationLoop;
        _loggerService = loggerService;
        _emulatorStateSerializer = emulatorStateSerializer;
        _pauseHandler = pauseHandler;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _dumpContext = dumpContext;
        _gdbServer = CreateGdbServer(configuration, memory, memoryDataExporter, functionHandlerProvider,
            state, functionCatalogue,
            executionDumpFactory,
            emulatorBreakpointsManager, pauseHandler, dumpContext, _loggerService);

        if (configuration.InitializeDOS is null) {
            configuration.InitializeDOS = true;
            if (loggerService.IsEnabled(LogEventLevel.Verbose)) {
                loggerService.Verbose("InitializeDOS parameter not provided. Defaulting to true for EXEC path.");
            }
        }

        if (screenPresenter is not null) {
            screenPresenter.UserInterfaceInitialized += Run;
        }
        LoadInitialProgram(configuration, memory, state, int21Handler);
    }

    /// <summary>
    /// Starts the loaded program.
    /// </summary>
    public void Run() {
        try {
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("Starting the emulation loop");
            }

            if (_configuration.Debug) {
                ToggleStartOrStopBreakpoint(BreakPointType.MACHINE_START,
                    "Starting the emulated program in paused state.");
                ToggleStartOrStopBreakpoint(BreakPointType.MACHINE_STOP,
                    "Stopping the emulated program in paused state.");
            }

            _gdbServer?.StartServer();
            _emulationLoop.Run();

            if (_configuration.DumpDataOnExit is not false) {
                _emulatorStateSerializer.SerializeEmulatorStateToDirectory(_dumpContext.DumpDirectory);
            }
        } finally {
            EmulationStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ToggleStartOrStopBreakpoint(BreakPointType type, string reason) {
        BreakPoint breakPoint = new UnconditionalBreakPoint(type, (breakpoint) => { _pauseHandler.RequestPause(reason); }, removeOnTrigger: false);
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

    private void LoadInitialProgram(Configuration configuration, IMemory memory, State state, DosInt21Handler int21Handler) {
        string? executableFileName = configuration.Exe;
        ArgumentException.ThrowIfNullOrEmpty(executableFileName);

        string upperCaseExtension = Path.GetExtension(executableFileName.ToUpperInvariant());
        bool isDosProgram = upperCaseExtension is ".EXE" or ".COM";

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Preparing initial load for {FileName} (DOS program: {IsDosProgram})", executableFileName, isDosProgram);
        }

        if (isDosProgram) {
            try {
                byte[] fileContent = File.ReadAllBytes(executableFileName);
                CheckSha256Checksum(fileContent, configuration.ExpectedChecksumValue);
            } catch (IOException e) {
                throw new UnrecoverableException($"Failed to read file {executableFileName}", e);
            }
            DosProgramLoader dosProgramLoader = new(configuration, memory, state, int21Handler, _loggerService);
            dosProgramLoader.LoadFile(executableFileName, configuration.ExeArgs);
        } else {
            BiosLoader loader = new(memory, state, _loggerService);
            try {
                byte[] fileContent = loader.LoadFile(executableFileName, configuration.ExeArgs);
                CheckSha256Checksum(fileContent, configuration.ExpectedChecksumValue);
            } catch (IOException e) {
                throw new UnrecoverableException($"Failed to read file {executableFileName}", e);
            }
        }
    }

    private static GdbServer? CreateGdbServer(Configuration configuration, IMemory memory,
        MemoryDataExporter memoryDataExporter, IFunctionHandlerProvider functionHandlerProvider, State state,
        FunctionCatalogue functionCatalogue, IExecutionDumpFactory executionDumpFactory,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        IPauseHandler pauseHandler, DumpFolderMetadata dumpContext, ILoggerService loggerService) {
        if (configuration.GdbPort == 0) {
            if (loggerService.IsEnabled(LogEventLevel.Information)) {
                loggerService.Information("GDB port is 0, disabling GDB server.");
            }
            return null;
        }
        return new GdbServer(configuration, memory, functionHandlerProvider, state, memoryDataExporter,
                functionCatalogue, executionDumpFactory,
            emulatorBreakpointsManager, pauseHandler, dumpContext, loggerService);
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