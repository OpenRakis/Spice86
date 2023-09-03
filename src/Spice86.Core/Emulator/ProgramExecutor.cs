namespace Spice86.Core.Emulator;

using Function.Dump;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.LoadableFile.Bios;
using Spice86.Core.Emulator.LoadableFile.Dos.Com;
using Spice86.Core.Emulator.LoadableFile.Dos.Exe;
using Spice86.Shared.Interfaces;

using System.Security.Cryptography;
using System.Diagnostics;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Loads and executes a program following the given configuration in the emulator.<br/>
/// Currently only supports DOS EXE and COM files.
/// </summary>
public sealed class ProgramExecutor : IDisposable {
    private readonly ILoggerService _loggerService;
    private bool _disposed;
    private readonly Configuration _configuration;
    private readonly GdbServer? _gdbServer;
    private readonly EmulationLoop _emulationLoop;
    
    private bool ListensToBreakpoints => _configuration.GdbPort != null || _configuration.DumpDataOnExit is not false;
    
    /// <summary>
    /// Initializes a new instance of <see cref="ProgramExecutor"/>
    /// </summary>
    /// <param name="loggerService">The logging service to use. Provided via DI.</param>
    /// <param name="gui">The GUI to use for user actions. Can be null for headless mode or unit tests.</param>
    /// <param name="configuration">The emulator <see cref="Configuration"/> to use.</param>
    public ProgramExecutor(ILoggerService loggerService, IGui? gui, Configuration configuration) {
        _loggerService = loggerService;
        _configuration = configuration;
        Machine = CreateMachine(gui);
        _gdbServer = CreateGdbServer(gui);
        _emulationLoop = new(loggerService, Machine.Cpu, Machine.CpuState, Machine.Timer, ListensToBreakpoints, Machine.MachineBreakpoints, Machine.DmaController, _gdbServer?.GdbCommandHandler);
    }

    /// <summary>
    /// The emulator machine.
    /// </summary>
    public Machine Machine { get; private set; }

    /// <summary>
    /// Begins the emulation process.
    /// </summary>
    public void Run() {
        _gdbServer?.StartServerAndWait();
        _emulationLoop.Run();
        if (ListensToBreakpoints) {
            DumpEmulatorStateToDirectory(_configuration.RecordedDataDirectory);
        }
    }

    public State CpuState => Machine.Cpu.State;

    public VideoState VideoState => Machine.VgaRegisters;

    public ArgbPalette ArgbPalette => Machine.VgaRegisters.DacRegisters.ArgbPalette;

    public IVgaRenderer VgaRenderer => Machine.VgaRenderer;

    public void DumpEmulatorStateToDirectory(string path) {
        new RecorderDataWriter(Machine.Memory,
                Machine.Cpu.State, Machine.CallbackHandler, _configuration,
                Machine.Cpu.ExecutionFlowRecorder,
                path, _loggerService)
            .DumpAll(Machine.Cpu.ExecutionFlowRecorder, Machine.Cpu.FunctionHandler);
    }

    public bool IsPaused { get => _emulationLoop.IsPaused; set => _emulationLoop.IsPaused = value; }

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

    public IVideoCard VideoCard => Machine.VgaCard;

    private ExecutableFileLoader CreateExecutableFileLoader(Configuration configuration) {
        string? executableFileName = configuration.Exe;
        ArgumentException.ThrowIfNullOrEmpty(executableFileName);

        string lowerCaseFileName = executableFileName.ToLowerInvariant();
        ushort entryPointSegment = (ushort)configuration.ProgramEntryPointSegment;
        if (lowerCaseFileName.EndsWith(".exe")) {
            return new ExeLoader(Machine.Memory,
                Machine.Cpu.State,
                _loggerService,
                Machine.Dos.EnvironmentVariables,
                Machine.Dos.FileManager,
                Machine.Dos.MemoryManager,
                entryPointSegment);
        }

        if (lowerCaseFileName.EndsWith(".com")) {
            return new ComLoader(Machine.Memory,
                Machine.Cpu.State,
                _loggerService,
                Machine.Dos.EnvironmentVariables,
                Machine.Dos.FileManager,
                Machine.Dos.MemoryManager,
                entryPointSegment);
        }

        return new BiosLoader(Machine.Memory, Machine.Cpu.State, _loggerService);
    }

    private Machine CreateMachine(IGui? gui) {
        CounterConfigurator counterConfigurator = new CounterConfigurator(_configuration, _loggerService);
        RecordedDataReader reader = new RecordedDataReader(_configuration.RecordedDataDirectory, _loggerService);
        ExecutionFlowRecorder executionFlowRecorder = reader.ReadExecutionFlowRecorderFromFileOrCreate(ListensToBreakpoints);
        Machine = new Machine(gui, _loggerService, counterConfigurator, executionFlowRecorder, _configuration, ListensToBreakpoints);
        InitializeCpu();
        ExecutableFileLoader loader = CreateExecutableFileLoader(_configuration);
        if (_configuration.InitializeDOS is null) {
            _configuration.InitializeDOS = loader.DosInitializationNeeded;
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("InitializeDOS parameter not provided. Guessed value is: {InitializeDOS}", _configuration.InitializeDOS);
            }
        }

        if (_configuration.InitializeDOS is true) {
            // Initialize VGA text mode.
            Machine.VgaFunctions.VgaSetMode(0x03, ModeFlags.Legacy);
            // Put HLT at the reset address
            Machine.Memory.UInt16[0xF000, 0xFFF0] = 0xF4;
        } else {
            // Bios will take care of enabling interrupts (or not)
            Machine.DualPic.MaskAllInterrupts();
        }

        InitializeFunctionHandlers(_configuration, reader.ReadGhidraSymbolsFromFileOrCreate());
        LoadFileToRun(_configuration, loader);
        executionFlowRecorder.PreAllocatePossibleExecutionFlowBreakPoints(Machine.Memory, Machine.Cpu.State);
        return Machine;
    }

    private GdbServer? CreateGdbServer(IGui? gui) {
        int? gdbPort = _configuration.GdbPort;
        if (gdbPort != null) {
            return new GdbServer(Machine.Memory, Machine.Cpu,
                Machine.Cpu.State, Machine.CallbackHandler, Machine.Cpu.FunctionHandler,
                Machine.Cpu.ExecutionFlowRecorder,
                Machine.MachineBreakpoints,
                Machine.MachineBreakpoints.PauseHandler,
                _loggerService,
                _configuration,
                gui);
        }

        return null;
    }

    private Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        IOverrideSupplier? supplier, ushort entryPointSegment, Machine machine) {
        Dictionary<SegmentedAddress, FunctionInformation> res = new();
        if (supplier != null) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("Override supplied: {OverrideSupplier}", supplier);
            }

            foreach (KeyValuePair<SegmentedAddress, FunctionInformation> element in supplier
                    .GenerateFunctionInformations(_loggerService, _configuration, entryPointSegment, machine)) {
                res.Add(element.Key, element.Value);
            }
        }

        return res;
    }

    private void InitializeCpu() {
        Cpu cpu = Machine.Cpu;
        cpu.ErrorOnUninitializedInterruptHandler = true;
        State state = cpu.State;
        state.Flags.IsDOSBoxCompatible = true;
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
        ArgumentException.ThrowIfNullOrEmpty(executableFileName);

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Loading file {FileName} with loader {LoaderType}", executableFileName,
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

    private static void SetupFunctionHandler(FunctionHandler functionHandler,
        IDictionary<SegmentedAddress, FunctionInformation> functionInformations, bool useCodeOverride) {
        functionHandler.FunctionInformations = functionInformations;
        functionHandler.UseCodeOverride = useCodeOverride;
    }
}