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

/// <summary>
/// Loads and executes a program following the given configuration in the emulator.<br/>
/// Currently only supports DOS exe files.
/// </summary>
public class ProgramExecutor : IDisposable {
    private static readonly ILogger _logger = Log.Logger.ForContext<ProgramExecutor>();
    private bool disposedValue;
    private GdbServer gdbServer;
    private Machine machine;

    public ProgramExecutor(Gui gui, Configuration? configuration) {
        CreateMachine(gui, configuration);
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public Machine GetMachine() {
        return machine;
    }

    public void Run() {
        machine.Run();
    }

    protected void Dispose(bool disposing) {
        if (!disposedValue) {
            if (disposing) {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    private void CheckSha256Checksum(byte[] file, byte[] expectedHash) {
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

    private ExecutableFileLoader CreateExecutableFileLoader(string fileName, int entryPointSegment) {
        string lowerCaseFileName = fileName.ToLowerInvariant();
        if (lowerCaseFileName.EndsWith(".exe")) {
            return new ExeLoader(machine, entryPointSegment);
        } else if (lowerCaseFileName.EndsWith(".com")) {
            return new ComLoader(machine, entryPointSegment);
        }

        return new BiosLoader(machine);
    }

    private void CreateMachine(Gui gui, Configuration configuration) {
        CounterConfigurator counterConfigurator = new CounterConfigurator(configuration);
        bool debugMode = configuration.GetGdbPort() != null;
        machine = new Machine(gui, counterConfigurator, configuration.IsFailOnUnhandledPort(), debugMode);
        InitializeCpu();
        InitializeDos(configuration);
        if (configuration.IsInstallInterruptVector()) {
            // Doing this after function Handler init so that custom code there can have a chance to register some callbacks
            // if needed
            machine.InstallAllCallbacksInInterruptTable();
        }

        InitializeFunctionHandlers(configuration);
        LoadFileToRun(configuration);
        StartGdbServer(configuration);
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
        string? exe = configuration.GetExe();
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
        Cpu cpu = machine.GetCpu();
        cpu.SetErrorOnUninitializedInterruptHandler(true);
        State state = cpu.GetState();
        state.GetFlags().SetDosboxCompatibility(true);
    }

    private void InitializeDos(Configuration configuration) {
        string parentFolder = GetExeParentFolder(configuration);
        Dictionary<char, string> driveMap = new();
        string cDrive = configuration.GetcDrive();
        if (string.IsNullOrWhiteSpace(cDrive)) {
            cDrive = parentFolder;
        }

        driveMap.Add('C', cDrive);
        machine.GetDosInt21Handler().GetDosFileManager().SetDiskParameters(parentFolder, driveMap);
    }

    private void InitializeFunctionHandlers(Configuration configuration) {
        Cpu cpu = machine.GetCpu();
        Dictionary<SegmentedAddress, FunctionInformation> functionInformations = GenerateFunctionInformations(configuration.GetOverrideSupplier(), configuration.GetProgramEntryPointSegment(), machine);
        bool useCodeOverride = configuration.IsUseCodeOverride();
        SetupFunctionHandler(cpu.GetFunctionHandler(), functionInformations, useCodeOverride);
        SetupFunctionHandler(cpu.GetFunctionHandlerInExternalInterrupt(), functionInformations, useCodeOverride);
    }

    private void LoadFileToRun(Configuration configuration) {
        string fileName = configuration.GetExe();
        ExecutableFileLoader loader = CreateExecutableFileLoader(fileName, configuration.GetProgramEntryPointSegment());
        _logger.Information("Loading file {@FileName} with loader {@LoaderType}", fileName, loader.GetType());
        try {
            byte[] fileContent = loader.LoadFile(fileName, configuration.GetExeArgs());
            CheckSha256Checksum(fileContent, configuration.GetExpectedChecksum());
        } catch (IOException e) {
            throw new UnrecoverableException("Failed to read file " + fileName, e);
        }
    }

    private void SetupFunctionHandler(FunctionHandler functionHandler, Dictionary<SegmentedAddress, FunctionInformation> functionInformations, bool useCodeOverride) {
        functionHandler.SetFunctionInformations(functionInformations);
        functionHandler.SetUseCodeOverride(useCodeOverride);
    }

    private void StartGdbServer(Configuration configuration) {
        int? gdbPort = configuration.GetGdbPort();
        if (gdbPort != null) {
            gdbServer = new GdbServer(machine, gdbPort.Value, configuration.GetDefaultDumpDirectory());
        }
    }
}