namespace Spice86.Core.Emulator;

using Function.Dump;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.LoadableFile.Bios;
using Spice86.Core.Emulator.LoadableFile.Dos.Com;
using Spice86.Core.Emulator.LoadableFile.Dos.Exe;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Security.Cryptography;

/// <inheritdoc cref="IProgramExecutor"/>
public sealed class ProgramExecutor : IProgramExecutor {
    private readonly ILoggerService _loggerService;
    private bool _disposed;
    private readonly Configuration _configuration;
    private readonly GdbServer? _gdbServer;
    private readonly EmulationLoop _emulationLoop;
    private readonly RecorderDataWriter _recorderDataWriter;
    private GdbCommandHandler? _gdbCommandHandler;

    /// <summary>
    /// Initializes a new instance of <see cref="ProgramExecutor"/>
    /// </summary>
    /// <param name="configuration">The emulator <see cref="Configuration"/> to use.</param>
    /// <param name="loggerService">The logging service to use. Provided via DI.</param>
    /// <param name="gui">The GUI to use for user actions. Can be null for headless mode or unit tests.</param>
    public ProgramExecutor(Configuration configuration, ILoggerService loggerService, IGui? gui) {
        _configuration = configuration;
        _loggerService = loggerService;
        PauseHandler pauseHandler = new(_loggerService);
        Machine = CreateMachine(pauseHandler, configuration, gui);
        _recorderDataWriter = new RecorderDataWriter(Machine.Cpu.ExecutionFlowRecorder,
            Machine.Cpu.State,
            new MemoryDataExporter(Machine.Memory, Machine.CallbackHandler, _configuration,
                _configuration.RecordedDataDirectory, _loggerService),
            new ExecutionFlowDumper(_loggerService),
            _loggerService,
            _configuration.RecordedDataDirectory);
        _gdbServer = CreateGdbServer(pauseHandler);
        _emulationLoop = new(loggerService, Machine.Cpu.FunctionHandler, Machine.Cpu, Machine.CpuState, Machine.Timer, Machine.MachineBreakpoints, Machine.DmaController, _gdbCommandHandler);
    }

    /// <summary>
    /// The emulator machine.
    /// </summary>
    public Machine Machine { get; private set; }

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
        IsPaused = false;
    }

    /// <inheritdoc/>
    public void DumpEmulatorStateToDirectory(string path) {
        new RecorderDataWriter(Machine.Cpu.ExecutionFlowRecorder,
                Machine.Cpu.State,
                new MemoryDataExporter(Machine.Memory, Machine.CallbackHandler, _configuration, path, _loggerService),
                new ExecutionFlowDumper(_loggerService),
                _loggerService,
                path)
            .DumpAll(Machine.Cpu.ExecutionFlowRecorder, Machine.Cpu.FunctionHandler);
    }

    /// <inheritdoc/>
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

    private ExecutableFileLoader CreateExecutableFileLoader(Configuration configuration) {
        string? executableFileName = configuration.Exe;
        ArgumentException.ThrowIfNullOrEmpty(executableFileName);

        string lowerCaseFileName = executableFileName.ToLowerInvariant();
        ushort entryPointSegment = configuration.ProgramEntryPointSegment;
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

    private Machine CreateMachine(PauseHandler pauseHandler, Configuration configuration, IGui? gui) {
        RecordedDataReader reader = new(_configuration.RecordedDataDirectory, _loggerService);
        ExecutionFlowRecorder executionFlowRecorder = reader.ReadExecutionFlowRecorderFromFileOrCreate(_configuration.DumpDataOnExit is not false);
        State cpuState = new(new Flags(), new GeneralRegisters(), new SegmentRegisters());
        IOPortDispatcher ioPortDispatcher = new(cpuState, _loggerService, _configuration.FailOnUnhandledPort);
        IMemory memory = new Emulator.Memory.Memory(new MemoryBreakpoints(), new Ram(A20Gate.EndOfHighMemoryArea),new A20Gate(!configuration.A20Gate));
        MachineBreakpoints machineBreakpoints = new(pauseHandler, new BreakPointHolder(), new BreakPointHolder(), memory, cpuState);

        Machine = new Machine(gui, memory, machineBreakpoints,
            cpuState, ioPortDispatcher, _loggerService,
            executionFlowRecorder, _configuration, _configuration.DumpDataOnExit is not false);

        ExecutableFileLoader loader = CreateExecutableFileLoader(_configuration);
        if (_configuration.InitializeDOS is null) {
            _configuration.InitializeDOS = loader.DosInitializationNeeded;
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("InitializeDOS parameter not provided. Guessed value is: {InitializeDOS}", _configuration.InitializeDOS);
            }
        }
        InitializeFunctionHandlers(_configuration, reader.ReadGhidraSymbolsFromFileOrCreate());
        LoadFileToRun(_configuration, loader);
        return Machine;
    }

    private GdbServer? CreateGdbServer(PauseHandler pauseHandler) {
        int? gdbPort = _configuration.GdbPort;
        if (gdbPort == null) {
            return null;
        }
        GdbIo gdbIo = new(gdbPort.Value, _loggerService);
        GdbFormatter gdbFormatter = new();
        var gdbCommandRegisterHandler = new GdbCommandRegisterHandler(Machine.Cpu.State, gdbFormatter, gdbIo, _loggerService);
        var gdbCommandMemoryHandler = new GdbCommandMemoryHandler(Machine.Memory, gdbFormatter, gdbIo, _loggerService);
        var gdbCommandBreakpointHandler = new GdbCommandBreakpointHandler(Machine.MachineBreakpoints, pauseHandler, gdbIo, _loggerService);
        var gdbCustomCommandsHandler = new GdbCustomCommandsHandler(Machine.Memory, Machine.Cpu.State, Machine.Cpu,
            Machine.MachineBreakpoints, _recorderDataWriter, gdbIo,
            _loggerService,
            gdbCommandBreakpointHandler.OnBreakPointReached);
        _gdbCommandHandler = new(gdbCommandBreakpointHandler, gdbCommandMemoryHandler, gdbCommandRegisterHandler, gdbCustomCommandsHandler,
            Machine.Cpu.State,
            pauseHandler,
            Machine.Cpu.ExecutionFlowRecorder,
            Machine.Cpu.FunctionHandler,
            gdbIo,
            _loggerService);
        return new GdbServer(
            gdbIo,
            Machine.Cpu.State,
            pauseHandler,
            _gdbCommandHandler,
            _loggerService,
            _configuration);
    }

    private Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        IOverrideSupplier? supplier, ushort entryPointSegment, Machine machine) {
        Dictionary<SegmentedAddress, FunctionInformation> res = new();
        if (supplier == null) {
            return res;
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Override supplied: {OverrideSupplier}", supplier);
        }

        foreach (KeyValuePair<SegmentedAddress, FunctionInformation> element in supplier
                    .GenerateFunctionInformations(_loggerService, _configuration, entryPointSegment, machine)) {
            res.Add(element.Key, element.Value);
        }

        return res;
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
            throw new UnrecoverableException($"Failed to read file {executableFileName}", e);
        }
    }

    private static void SetupFunctionHandler(FunctionHandler functionHandler,
        IDictionary<SegmentedAddress, FunctionInformation> functionInformations, bool useCodeOverride) {
        functionHandler.FunctionInformations = functionInformations;
        functionHandler.UseCodeOverride = useCodeOverride;
    }

    /// <inheritdoc/>
    public void Accept<T>(T emulatorDebugger) where T : IInternalDebugger {
        emulatorDebugger.Visit(this);
        Machine.Accept(emulatorDebugger);
    }
}