namespace Spice86.Emulator;

using Function.Dump;

using Serilog;

using Spice86.Emulator.CPU;
using Spice86.Emulator.Devices.Timer;
using Spice86.Emulator.Errors;
using Spice86.Emulator.Function;
using Spice86.Emulator.Gdb;
using Spice86.Emulator.LoadableFile;
using Spice86.Emulator.LoadableFile.Bios;
using Spice86.Emulator.LoadableFile.Dos.Com;
using Spice86.Emulator.LoadableFile.Dos.Exe;
using Spice86.Emulator.Memory;
using Spice86.Emulator.VM;
using Spice86.UI.ViewModels;
using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

/// <summary>
/// Loads and executes a program following the given configuration in the emulator.<br/>
/// Currently only supports DOS exe files.
/// </summary>
public class ProgramExecutor : IDisposable {
    private static readonly ILogger _logger = Program.Logger.ForContext<ProgramExecutor>();
    private bool _disposedValue;
    private readonly GdbServer? _gdbServer;

    public ProgramExecutor(MainWindowViewModel? gui, Configuration? configuration) {
        if (configuration == null) {
            throw new ArgumentNullException(nameof(configuration));
        }
        Machine = CreateMachine(gui, configuration);
        _gdbServer = StartGdbServer(configuration);
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public Machine Machine { get; private set; }

    public void Run() {
        Machine.Run();
    }

    protected void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                _gdbServer?.Dispose();
                Machine.Dispose();
            }
            _disposedValue = true;
        }
    }

    private static void CheckSha256Checksum(byte[] file, byte[]? expectedHash) {
        if (expectedHash is null) {
            throw new ArgumentNullException(nameof(expectedHash));
        }
        if (expectedHash.Length == 0) {
            // No hash check
            return;
        }

        try {
            using var mySHA256 = SHA256.Create();
            byte[] actualHash = mySHA256.ComputeHash(file);

            if (!actualHash.AsSpan().SequenceEqual(expectedHash)) {
                string error = $"File does not match the expected SHA256 checksum, cannot execute it.\nExpected checksum is {ConvertUtils.ByteArrayToHexString(expectedHash)}.\nGot {ConvertUtils.ByteArrayToHexString(actualHash)}\n";
                throw new UnrecoverableException(error);
            }
        } catch (UnauthorizedAccessException e) {
            throw new UnrecoverableException("Executable file hash calculation failed", e);
        }
    }

    private ExecutableFileLoader CreateExecutableFileLoader(string? fileName, int entryPointSegment) {
        if (fileName == null) {
            throw new ArgumentNullException(nameof(fileName));
        }
        string lowerCaseFileName = fileName.ToLowerInvariant();
        if (lowerCaseFileName.EndsWith(".exe")) {
            return new ExeLoader(Machine, (ushort)entryPointSegment);
        } else if (lowerCaseFileName.EndsWith(".com")) {
            return new ComLoader(Machine, (ushort)entryPointSegment);
        }

        return new BiosLoader(Machine);
    }

    private Machine CreateMachine(MainWindowViewModel? gui, Configuration? configuration) {
        if (configuration == null) {
            throw new ArgumentNullException(nameof(configuration));
        }
        var counterConfigurator = new CounterConfigurator(configuration);
        bool debugMode = configuration.GdbPort != null;
        ExecutionFlowRecorder executionFlowRecorder = new ExecutionFlowDumper().ReadFromFileOrCreate(configuration.JumpFile);
        executionFlowRecorder.DebugMode = debugMode;
        Machine = new Machine(this, gui, counterConfigurator, executionFlowRecorder, configuration, debugMode);
        InitializeCpu();
        InitializeDos(configuration);
        if (configuration.InstallInterruptVector) {
            // Doing this after function Handler init so that custom code there can have a chance to register some callbacks
            // if needed
            Machine.InstallAllCallbacksInInterruptTable();
        }

        InitializeFunctionHandlers(configuration);
        LoadFileToRun(configuration);
        return Machine;
    }


    private GdbServer? StartGdbServer(Configuration configuration) {
        int? gdbPort = configuration.GdbPort;
        if (gdbPort != null) {
            var gdbServer = new GdbServer(Machine, configuration);
            return gdbServer;
        }
        return null;
    }

    private static Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(IOverrideSupplier? supplier, int entryPointSegment, Machine machine) {
        Dictionary<SegmentedAddress, FunctionInformation> res = new();
        if (supplier != null) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("Override supplied: {@OverideSupplier}", supplier);
            }
            foreach (KeyValuePair<SegmentedAddress, FunctionInformation> element in supplier.GenerateFunctionInformations(entryPointSegment, machine)) {
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
        if (parentDir == null) {
            // Must be in the current directory
            parentDir = new DirectoryInfo(Environment.CurrentDirectory);
        }

        string parent = Path.GetFullPath(parentDir.FullName);
        parent = parent.Replace('\\', '/') + '/';
        return parent;
    }

    private void InitializeCpu() {
        Cpu cpu = Machine.Cpu;
        cpu.ErrorOnUninitializedInterruptHandler = true;
        State state = cpu.State;
        state.Flags.IsDOSBoxCompatible = true;
    }

    private void InitializeDos(Configuration configuration) {
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

    private void InitializeFunctionHandlers(Configuration configuration) {
        IDictionary<SegmentedAddress, FunctionInformation> functionInformations = new Dictionary<SegmentedAddress, FunctionInformation>();
        if (configuration.SymbolsFile != null) {
            // Extract functions with names from symbols
            DictionaryUtils.AddAll(functionInformations, new GhidraSymbolsDumper().ReadFromFileOrCreate(configuration.SymbolsFile));
        }
        if (configuration.OverrideSupplier != null) {
            DictionaryUtils.AddAll(functionInformations, GenerateFunctionInformations(configuration.OverrideSupplier, configuration.ProgramEntryPointSegment, Machine));
        }
        if (functionInformations.Count == 0) {
            return;
        }
        Cpu cpu = Machine.Cpu;
        bool useCodeOverride = configuration.UseCodeOverride;
        SetupFunctionHandler(cpu.FunctionHandler, functionInformations, useCodeOverride);
        SetupFunctionHandler(cpu.FunctionHandlerInExternalInterrupt, functionInformations, useCodeOverride);
    }

    private void LoadFileToRun(Configuration configuration) {
        string? executableFileName = configuration.Exe;
        if (executableFileName is null) {
            throw new ArgumentNullException(nameof(executableFileName));
        }
        ExecutableFileLoader loader = CreateExecutableFileLoader(executableFileName, configuration.ProgramEntryPointSegment);
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Loading file {@FileName} with loader {@LoaderType}", executableFileName, loader.GetType());
        }
        try {
            byte[] fileContent = loader.LoadFile(executableFileName, configuration.ExeArgs);
            CheckSha256Checksum(fileContent, configuration.ExpectedChecksumValue);
        } catch (IOException e) {
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

    private static void SetupFunctionHandler(FunctionHandler functionHandler, IDictionary<SegmentedAddress, FunctionInformation> functionInformations, bool useCodeOverride) {
        functionHandler.FunctionInformations = functionInformations;
        functionHandler.UseCodeOverride = useCodeOverride;
    }
}