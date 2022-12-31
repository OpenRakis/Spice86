using Spice86.Core.DI;

namespace Spice86.Core.Emulator;

using Function.Dump;

using Serilog;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Utils;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.LoadableFile.Bios;
using Spice86.Core.Emulator.LoadableFile.Dos.Com;
using Spice86.Core.Emulator.LoadableFile.Dos.Exe;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Diagnostics;

using Spice86.Core.CLI;

/// <summary>
/// Loads and executes a program following the given configuration in the emulator.<br/>
/// Currently only supports DOS EXE and COM files.
/// </summary>
public sealed class ProgramExecutor : IDisposable {
    private readonly ILogger _logger;
    private bool _disposedValue;
    private readonly Configuration _configuration;
    private readonly GdbServer? _gdbServer;
    private bool RecordData => _configuration.GdbPort != null || _configuration.DumpDataOnExit is not false;

    public ProgramExecutor(ILogger logger, IGui? gui, IKeyScanCodeConverter? keyScanCodeConverter, Configuration configuration) {
        _logger = logger;
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Machine = CreateMachine(gui, keyScanCodeConverter);
        _gdbServer = StartGdbServer();
    }

    public Machine Machine { get; private set; }

    public void Run() {
        Machine.Run();
        if (RecordData) {
            new RecorderDataWriter(_configuration.RecordedDataDirectory, Machine,
                new ServiceProvider().GetLoggerForContext<RecorderDataWriter>())
                .DumpAll();
        }
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                _gdbServer?.Dispose();
                Machine.Dispose();
            }

            _disposedValue = true;
        }
    }

    private static void CheckSha256Checksum(byte[] file, byte[]? expectedHash) {
        ArgumentNullException.ThrowIfNull(expectedHash, nameof(expectedHash));
        if (expectedHash.Length == 0) {
            // No hash check
            return;
        }

        try {
            byte[] actualHash = SHA256.HashData(file);

            if (!actualHash.AsSpan().SequenceEqual(expectedHash)) {
                string error =
                    $"File does not match the expected SHA256 checksum, cannot execute it.\nExpected checksum is {ConvertUtils.ByteArrayToHexString(expectedHash)}.\nGot {ConvertUtils.ByteArrayToHexString(actualHash)}\n";
                throw new UnrecoverableException(error);
            }
        } catch (UnauthorizedAccessException e) {
            e.Demystify();
            throw new UnrecoverableException("Executable file hash calculation failed", e);
        }
    }

    private ExecutableFileLoader CreateExecutableFileLoader(Configuration configuration) {
        string? executableFileName = configuration.Exe;
        ArgumentException.ThrowIfNullOrEmpty(executableFileName);

        string lowerCaseFileName = executableFileName.ToLowerInvariant();
        ushort entryPointSegment = (ushort)configuration.ProgramEntryPointSegment;
        if (lowerCaseFileName.EndsWith(".exe")) {
            return new ExeLoader(
                Machine,
                new ServiceProvider().GetLoggerForContext<ExeLoader>(),
                entryPointSegment);
        }

        if (lowerCaseFileName.EndsWith(".com")) {
            return new ComLoader(Machine, entryPointSegment);
        }

        return new BiosLoader(Machine);
    }

    private Machine CreateMachine(IGui? gui, IKeyScanCodeConverter? keyScanCodeConverter) {
        CounterConfigurator counterConfigurator = new CounterConfigurator(_configuration, new ServiceProvider().GetLoggerForContext<CounterConfigurator>());
        RecordedDataReader reader = new RecordedDataReader(_configuration.RecordedDataDirectory);
        ExecutionFlowRecorder executionFlowRecorder = reader.ReadExecutionFlowRecorderFromFileOrCreate(RecordData);
        Machine = new Machine(this, gui, keyScanCodeConverter, counterConfigurator, executionFlowRecorder,
            _configuration, RecordData);
        InitializeCpu();
        ExecutableFileLoader loader = CreateExecutableFileLoader(_configuration);
        if (_configuration.InitializeDOS is null) {
            _configuration.InitializeDOS = loader.DosInitializationNeeded;
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("InitializeDOS parameter not provided. Guessed value is: {@InitializeDOS}", _configuration.InitializeDOS);
            }
        }

        if (_configuration.InitializeDOS is true) {
            InitializeDOS(_configuration);
            // Doing this after function Handler init so that custom code there can have a chance to register some callbacks
            // if needed
            Machine.InstallAllCallbacksInInterruptTable();
            // Put HLT at the reset address
            Machine.Memory.UInt16[0xF000, 0xFFF0] = 0xF4;
        } else {
            // Bios will take care of enabling interrupts (or not)
            Machine.DualPic.MaskAllInterrupts();
        }

        InitializeFunctionHandlers(_configuration, reader.ReadGhidraSymbolsFromFileOrCreate());
        LoadFileToRun(_configuration, loader);
        return Machine;
    }

    private GdbServer? StartGdbServer() {
        int? gdbPort = _configuration.GdbPort;
        if (gdbPort != null) {
            return new GdbServer(Machine,
                new ServiceProvider().GetLoggerForContext<GdbServer>(),
                _configuration);
        }

        return null;
    }

    private Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        IOverrideSupplier? supplier, int entryPointSegment, Machine machine) {
        Dictionary<SegmentedAddress, FunctionInformation> res = new();
        if (supplier != null) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("Override supplied: {@OverideSupplier}", supplier);
            }

            foreach (KeyValuePair<SegmentedAddress, FunctionInformation> element in supplier
                         .GenerateFunctionInformations(entryPointSegment, machine)) {
                res.Add(element.Key, element.Value);
            }
        }

        return res;
    }

    private static string? GetExeParentFolder(Configuration configuration) {
        string? exe = configuration.Exe;
        if (exe == null) {
            return null;
        }

        DirectoryInfo? parentDir = Directory.GetParent(exe);
        // Must be in the current directory
        parentDir ??= new DirectoryInfo(Environment.CurrentDirectory);

        string parent = Path.GetFullPath(parentDir.FullName);
        return parent.Replace('\\', '/') + '/';
    }

    private void InitializeCpu() {
        Cpu cpu = Machine.Cpu;
        cpu.ErrorOnUninitializedInterruptHandler = true;
        State state = cpu.State;
        state.Flags.IsDOSBoxCompatible = true;
    }

    private void InitializeDOS(Configuration configuration) {
        string? parentFolder = GetExeParentFolder(configuration);
        Dictionary<char, string> driveMap = new();
        string? cDrive = configuration.CDrive;
        if (string.IsNullOrWhiteSpace(cDrive)) {
            cDrive = parentFolder;
        }

        if (string.IsNullOrWhiteSpace(cDrive)) {
            throw new ArgumentNullException(nameof(cDrive));
        }

        cDrive = ConvertUtils.ToSlashFolderPath(cDrive);
        if (string.IsNullOrWhiteSpace(parentFolder)) {
            throw new ArgumentNullException(nameof(parentFolder));
        }

        driveMap.Add('C', cDrive);
        Machine.DosInt21Handler.DosFileManager.SetDiskParameters(parentFolder, driveMap);
    }

    private void InitializeFunctionHandlers(Configuration configuration,
        IDictionary<SegmentedAddress, FunctionInformation> functionInformations) {
        if (configuration.OverrideSupplier != null) {
            DictionaryUtils.AddAll(functionInformations,
                GenerateFunctionInformations(configuration.OverrideSupplier, configuration.ProgramEntryPointSegment,
                    Machine));
        }

        if (functionInformations.Count == 0) {
            return;
        }

        Cpu cpu = Machine.Cpu;
        bool useCodeOverride = configuration.UseCodeOverrideOption;
        SetupFunctionHandler(cpu.FunctionHandler, functionInformations, useCodeOverride);
        SetupFunctionHandler(cpu.FunctionHandlerInExternalInterrupt, functionInformations, useCodeOverride);
    }

    private void LoadFileToRun(Configuration configuration, ExecutableFileLoader loader) {
        string? executableFileName = configuration.Exe;
        if (executableFileName is null) {
            throw new ArgumentNullException(nameof(executableFileName));
        }

        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Loading file {@FileName} with loader {@LoaderType}", executableFileName,
                loader.GetType());
        }

        try {
            byte[] fileContent = loader.LoadFile(executableFileName, configuration.ExeArgs);
            CheckSha256Checksum(fileContent, configuration.ExpectedChecksumValue);
        } catch (IOException e) {
            e.Demystify();
            throw new UnrecoverableException($"Failed to read file {executableFileName}", e);
        }
    }

    internal bool Step() {
        if (_gdbServer?.GdbCommandHandler is null) {
            return false;
        }

        _gdbServer.GdbCommandHandler.Step();
        return true;
    }

    private static void SetupFunctionHandler(FunctionHandler functionHandler,
        IDictionary<SegmentedAddress, FunctionInformation> functionInformations, bool useCodeOverride) {
        functionHandler.FunctionInformations = functionInformations;
        functionHandler.UseCodeOverride = useCodeOverride;
    }
}