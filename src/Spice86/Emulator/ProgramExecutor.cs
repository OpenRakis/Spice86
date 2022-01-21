namespace Spice86.Emulator;

using Serilog;

using System;
using System.Collections.Generic;
using System.IO;


using Spice86.Emulator.CPU;
using Spice86.Emulator.Gdb;
using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;
using Spice86.Utils;
using Spice86.UI;
using Spice86.Emulator.LoadableFile;
using Spice86.Emulator.Errors;
using Spice86.Emulator.Function;
using Spice86.Emulator.Devices.Timer;
using Spice86.Emulator.Loadablefile.Dos.Exe;
using Spice86.Emulator.Loadablefile.Dos.Com;
using Spice86.Emulator.Loadablefile.Bios;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Loads and executes a program following the given configuration in the emulator.<br/>
/// Currently only supports DOS exe files.
/// </summary>
public class ProgramExecutor : IDisposable {
    private static readonly ILogger _logger = Log.Logger.ForContext<ProgramExecutor>();
    private bool _disposedValue;
    private GdbServer? _gdbServer;
    private Machine _machine;

    public ProgramExecutor(Gui gui, Configuration? configuration) {
        if(configuration == null) {
            throw new ArgumentNullException(nameof(configuration));
        }
        _machine = CreateMachine(gui, configuration);
        _gdbServer = StartGdbServer(configuration);
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public Machine GetMachine() {
        return _machine;
    }

    public void Run() {
        _machine.Run();
    }

    protected void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                // TODO: dispose managed state (managed objects)
                _gdbServer?.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    private void CheckSha256Checksum(byte[] file, byte[]? expectedHash) {
        if(expectedHash is null) {
            throw new ArgumentNullException(nameof(expectedHash));
        }
        if (expectedHash.Length == 0) {
            return;
        }

        try {
            using SHA256 mySHA256 = SHA256.Create();
            byte[] actualHash = mySHA256.ComputeHash(file);

            if (!Array.Equals(expectedHash, actualHash)) {
                string error = "File does not match the expected SHA256 checksum, cannot execute it.\\n" + "Expected checksum is " + ConvertUtils.ByteArrayToHexString(expectedHash) + ".\\n" + "Got " + ConvertUtils.ByteArrayToHexString(actualHash) + "\\n";
                throw new UnrecoverableException(error);
            }
        } catch (UnauthorizedAccessException e) {
            throw new UnrecoverableException("Exectutable file hash calculation failed", e);
        }
    }

    private ExecutableFileLoader CreateExecutableFileLoader(string? fileName, int entryPointSegment) {
        if (fileName == null) {
            throw new ArgumentNullException(nameof(fileName));
        }
        string lowerCaseFileName = fileName.ToLowerInvariant();
        if (lowerCaseFileName.EndsWith(".exe")) {
            return new ExeLoader(_machine, entryPointSegment);
        } else if (lowerCaseFileName.EndsWith(".com")) {
            return new ComLoader(_machine, entryPointSegment);
        }

        return new BiosLoader(_machine);
    }

    private Machine CreateMachine(Gui gui, Configuration? configuration) {
        if (configuration == null) {
            throw new ArgumentNullException(nameof(configuration));
        }
        CounterConfigurator counterConfigurator = new CounterConfigurator(configuration);
        bool debugMode = configuration.GdbPort != null;
        var machine = new Machine(gui, counterConfigurator, configuration.FailOnUnhandledPort, debugMode);
        InitializeCpu();
        InitializeDos(configuration);
        if (configuration.InstallInterruptVector) {
            // Doing this after function Handler init so that custom code there can have a chance to register some callbacks
            // if needed
            machine.InstallAllCallbacksInInterruptTable();
        }

        InitializeFunctionHandlers(configuration);
        LoadFileToRun(configuration);
        return machine;
    }


    private GdbServer? StartGdbServer(Configuration configuration) {
        int? gdbPort = configuration.GdbPort;
        if (gdbPort != null) {
            var gdbServer = new GdbServer(_machine, gdbPort.Value, configuration.DefaultDumpDirectory);
            return gdbServer;
        }
        return null;
    }

    private Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(IOverrideSupplier supplier, int entryPointSegment, Machine machine) {
        Dictionary<SegmentedAddress, FunctionInformation> res = new();
        if (supplier != null) {
            _logger.Information("Override supplied: {@OverideSupplier}", supplier);
            foreach(KeyValuePair<SegmentedAddress, FunctionInformation> element in supplier.GenerateFunctionInformations(entryPointSegment, machine)) {
                res.Add(element.Key, element.Value);
            }
        }

        return res;
    }

    private string? GetExeParentFolder(Configuration configuration) {
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
        Cpu cpu = _machine.GetCpu();
        cpu.SetErrorOnUninitializedInterruptHandler(true);
        State state = cpu.GetState();
        state.GetFlags().SetDosboxCompatibility(true);
    }

    private void InitializeDos(Configuration configuration) {
        string? parentFolder = GetExeParentFolder(configuration);
        Dictionary<char, string> driveMap = new();
        string? cDrive = configuration.CDrive;
        if (string.IsNullOrWhiteSpace(cDrive)) {
            cDrive = parentFolder;
        }
        if(string.IsNullOrWhiteSpace(cDrive)) {
            throw new ArgumentNullException(nameof(cDrive));
        }
        if (string.IsNullOrWhiteSpace(parentFolder)) {
            throw new ArgumentNullException(nameof(parentFolder));
        }
        driveMap.Add('C', cDrive);
        _machine.GetDosInt21Handler().GetDosFileManager().SetDiskParameters(parentFolder, driveMap);
    }

    private void InitializeFunctionHandlers(Configuration configuration) {
        if(configuration.OverrideSupplier is null) {
            return;
        }
        Cpu cpu = _machine.GetCpu();
        Dictionary<SegmentedAddress, FunctionInformation> functionInformations = GenerateFunctionInformations(configuration.OverrideSupplier, configuration.ProgramEntryPointSegment, _machine);
        bool useCodeOverride = configuration.UseCodeOverride;
        SetupFunctionHandler(cpu.GetFunctionHandler(), functionInformations, useCodeOverride);
        SetupFunctionHandler(cpu.GetFunctionHandlerInExternalInterrupt(), functionInformations, useCodeOverride);
    }

    private void LoadFileToRun(Configuration configuration) {
        string? executableFileName = configuration.Exe;
        if(executableFileName is null) {
            throw new ArgumentNullException(nameof(executableFileName));
        }
        ExecutableFileLoader loader = CreateExecutableFileLoader(executableFileName, configuration.ProgramEntryPointSegment);
        _logger.Information("Loading file {@FileName} with loader {@LoaderType}", executableFileName, loader.GetType());
        try {
            var sb = new StringBuilder();
            if(configuration.ExeArgs != null) {
                foreach (var arg in configuration.ExeArgs) {
                    sb.Append(arg);
                }
            }
            var exeArgs = sb.ToString();
            byte[] fileContent = loader.LoadFile(executableFileName, exeArgs);
            CheckSha256Checksum(fileContent, configuration.ExpectedChecksumValue);
        } catch (IOException e) {
            throw new UnrecoverableException($"Failed to read file {executableFileName}", e);
        }
    }

    private void SetupFunctionHandler(FunctionHandler functionHandler, Dictionary<SegmentedAddress, FunctionInformation> functionInformations, bool useCodeOverride) {
        functionHandler.SetFunctionInformations(functionInformations);
        functionHandler.SetUseCodeOverride(useCodeOverride);
    }
}