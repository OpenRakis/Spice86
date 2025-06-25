﻿namespace Spice86.Core.Emulator;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.LoadableFile.Bios;
using Spice86.Core.Emulator.Memory;
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
    private readonly EmulatorStateSerializer _emulatorStateSerializer;
    private readonly DosInt21Handler _dosInt21Handler;
    private readonly State _state;
    private readonly IMemory _memory;

    /// <summary>
    /// Initializes a new instance of <see cref="ProgramExecutor"/>
    /// </summary>
    /// <param name="configuration">The emulator <see cref="Configuration"/> to use.</param>
    /// <param name="emulatorBreakpointsManager">The class that manages machine code execution breakpoints.</param>
    /// <param name="emulatorStateSerializer">The class that is responsible for serializing the state of the emulator to a directory.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="memoryDataExporter">The class used to dump main memory data properly.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="dosInt21handler">The DOS handler used for loading a DOS executable (.COM or .EXE).</param>
    /// <param name="functionCatalogue">List of all functions.</param>
    /// <param name="executionFlowRecorder">The class that records machine code execution flow.</param>
    /// <param name="pauseHandler">The object responsible for pausing and resuming the emulation.</param>
    /// <param name="screenPresenter">The user interface class that displays video output in a dedicated thread.</param>
    /// <param name="emulationLoop">The class that runs the core emulation process.</param>
    /// <param name="loggerService">The logging service to use.</param>
    public ProgramExecutor(Configuration configuration, FunctionCatalogue functionCatalogue,
        EmulatorBreakpointsManager emulatorBreakpointsManager, IMemory memory,
        EmulatorStateSerializer emulatorStateSerializer, State state,
        IFunctionHandlerProvider functionHandlerProvider, MemoryDataExporter memoryDataExporter,
        DosInt21Handler dosInt21handler, ExecutionFlowRecorder executionFlowRecorder,
        IPauseHandler pauseHandler, EmulationLoop emulationLoop,
        IScreenPresenter? screenPresenter, ILoggerService loggerService) {
        _configuration = configuration;
        _emulationLoop = emulationLoop;
        _loggerService = loggerService;
        _dosInt21Handler = dosInt21handler;
        _state = state;
        _memory = memory;
        _emulatorStateSerializer = emulatorStateSerializer;
        _pauseHandler = pauseHandler;
        if (configuration.GdbPort.HasValue) {
            _gdbServer = CreateGdbServer(configuration, memory, memoryDataExporter, functionHandlerProvider,
                state, functionCatalogue,
                executionFlowRecorder,
                emulatorBreakpointsManager, pauseHandler, _loggerService);
        }

        if (screenPresenter is not null) {
            screenPresenter.UserInterfaceInitialized += Run;
        }
        CheckFileToRun(configuration);
    }

    /// <summary>
    /// Starts the loaded program.
    /// </summary>
    public void Run() {
        if(_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Starting the emulation loop");
        }
        if (_gdbServer is not null) {
            _gdbServer?.StartServerAndWait();
        } else if (_configuration.Debug) {
            _pauseHandler.RequestPause("Starting the emulated program paused was requested");
        }
        if (string.IsNullOrWhiteSpace(_configuration.Exe)) {
            throw new UnrecoverableException("No file to execute");
        }
        string fileExtension = Path.GetExtension(_configuration.Exe).ToLowerInvariant();
        switch (fileExtension) {
            case ".exe" or ".com":
                _dosInt21Handler.LoadAndExecExeFile(_configuration.Exe, _configuration.ExeArgs, _configuration.ProgramEntryPointSegment);
                break;
            default:
                new BiosLoader(_memory, _state, _configuration.Exe, _loggerService).LoadHostFile();
                break;
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
                $"""
                 File does not match the expected SHA256 checksum, cannot execute it.
                 Expected checksum is {ConvertUtils.ByteArrayToHexString(expectedHash)}.
                 Got {ConvertUtils.ByteArrayToHexString(actualHash)}
                 """;
            throw new UnrecoverableException(error);
        }
    }

    private static GdbServer? CreateGdbServer(Configuration configuration, IMemory memory,
        MemoryDataExporter memoryDataExporter, IFunctionHandlerProvider functionHandlerProvider, State state,
        FunctionCatalogue functionCatalogue, ExecutionFlowRecorder executionFlowRecorder,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        IPauseHandler pauseHandler, ILoggerService loggerService) {
        if (configuration.GdbPort is null) {
            return null;
        }
        return new GdbServer(configuration, memory, functionHandlerProvider, state, memoryDataExporter,
                functionCatalogue, executionFlowRecorder,
            emulatorBreakpointsManager, pauseHandler, loggerService);
    }

    private void CheckFileToRun(Configuration configuration) {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.Exe);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Checking file {FileName}", configuration.Exe);
        }

        try {
            byte[] fileContent = File.ReadAllBytes(configuration.Exe);
            CheckSha256Checksum(fileContent, configuration.ExpectedChecksumValue);
        } catch (IOException e) {
            throw new UnrecoverableException($"Failed to read file {configuration.Exe}", e);
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