namespace Spice86.Tests;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Shared.Emulator.VM.Breakpoint;

using Xunit;
using Spice86.Audio.Filters;

public sealed class Spice86Creator : IDisposable {
    private readonly Configuration _configuration;
    private readonly long _maxCycles;
    private readonly string _exportFolder;
    private readonly bool _ownsExportFolder;

    public bool EnableUmb { get; set; } = true;

    public Spice86Creator(string binName, bool enablePit = false,
        long maxCycles = 100000, bool installInterruptVectors = false, bool failOnUnhandledPort = false, bool enableA20Gate = false,
        bool enableXms = false, bool enableEms = false, string? overrideSupplierClassName = null, string? cDrive = null,
        IOverrideSupplier? overrideSupplier = null,
        ushort programEntryPointSegment = 0x170,
        SbType sbType = SbType.None, OplMode oplMode = OplMode.None,
        ushort sbBase = 0x220, byte sbIrq = 7, byte sbDma = 1, byte sbHdma = 5,
        string? exeArgs = null, long? instructionTimeScale = null,
        JitMode jitMode = JitMode.InterpretedOnly, bool failOnInvalidOpcode = false,
        string? recordedDataDirectory = null, bool reloadCfgGraph = false,
        bool enableSpeculativeCfgExploration = true) : this(binName, enablePit, maxCycles,
            installInterruptVectors, failOnUnhandledPort, enableA20Gate, enableXms, enableEms,
            overrideSupplierClassName, cDrive, overrideSupplier, programEntryPointSegment, sbType, oplMode,
            sbBase, sbIrq, sbDma, sbHdma, exeArgs, instructionTimeScale, jitMode, failOnInvalidOpcode,
            recordedDataDirectory, reloadCfgGraph, enableSpeculativeCfgExploration, RenderingMode.Async, 0xF000) {
    }

    internal Spice86Creator(AsmFixtures.AsmRunSettings settings, JitMode jitMode, IOverrideSupplier? overrideSupplier)
        : this(settings.BinName, settings.EnablePit, settings.MaxCycles, settings.InstallInterruptVectors,
            settings.FailOnUnhandledPort, settings.EnableA20Gate, false, false, null, null, overrideSupplier,
            0x170, SbType.None, OplMode.None, 0x220, 7, 1, 5, null, null, jitMode, false, null, false,
            settings.EnableSpeculativeCfgExploration, settings.RenderingMode, settings.AsmHandlersSegment) {
    }

    private Spice86Creator(string binName, bool enablePit, long maxCycles, bool installInterruptVectors,
        bool failOnUnhandledPort, bool enableA20Gate, bool enableXms, bool enableEms,
        string? overrideSupplierClassName, string? cDrive, IOverrideSupplier? overrideSupplier,
        ushort programEntryPointSegment, SbType sbType, OplMode oplMode, ushort sbBase, byte sbIrq,
        byte sbDma, byte sbHdma, string? exeArgs, long? instructionTimeScale, JitMode jitMode,
        bool failOnInvalidOpcode, string? recordedDataDirectory, bool reloadCfgGraph,
        bool enableSpeculativeCfgExploration, RenderingMode renderingMode, ushort asmHandlersSegment) {
        string executablePath = Path.IsPathRooted(binName) ? binName : $"Resources/cpuTests/{binName}.bin";
        if (overrideSupplierClassName != null && overrideSupplier != null) {
            throw new ArgumentException("Provide either an override supplier instance or an override supplier class name, not both.");
        }
        if (overrideSupplierClassName != null) {
            overrideSupplier = CommandLineParser.ParseFunctionInformationSupplierClassName(overrideSupplierClassName);
        }

        // When the caller provides a recorded-data directory it owns the lifecycle (e.g. it must
        // survive across two Spice86Creator instances for a dump-then-reload handoff), so we do not
        // delete it on Dispose. Otherwise we create a throwaway folder and clean it up ourselves.
        _ownsExportFolder = recordedDataDirectory == null;
        string exportFolder = recordedDataDirectory ?? Path.Join(
            Path.GetTempPath(),
            Guid.NewGuid().ToString()
        );
        _exportFolder = exportFolder;
        _configuration = new Configuration {
            Exe = executablePath,
            // Don't expect any hash for the exe
            ExpectedChecksumValue = [],
            // when false: making sure int8 is not going to be triggered during the tests
            InitializeDOS = installInterruptVectors,
            ProvidedAsmHandlersSegment = asmHandlersSegment,
            ProgramEntryPointSegment = programEntryPointSegment,
            TimeMultiplier = enablePit ? 1 : 0,
            //Don"t need nor want to instantiate the UI in emulator unit tests
            HeadlessMode = HeadlessType.Minimal,
            Mouse = MouseType.Ps2,
            AudioEngine = AudioEngine.Dummy,
            SbType = sbType,
            SbBase = sbBase,
            SbIrq = sbIrq,
            SbDma = sbDma,
            SbHdma = sbHdma,
            OplMode = oplMode,
            FailOnUnhandledPort = failOnUnhandledPort,
            A20Gate = enableA20Gate,
            OverrideSupplier = overrideSupplier,
            Xms = enableXms,
            Ems = enableEms,
            CDrive = cDrive,
            ExeArgs = exeArgs,
            RecordedDataDirectory = exportFolder,
            SilencedLogs = true,
            HttpApiPort = 0,
            McpHttpPort = 0,
            // Deterministic cycle-based clock (CyclesClock) to avoid
            // wall-clock non-determinism in tests.
            // 333333 is the value that allowed most wall clock based tests to pass without changing all the expected values.
            InstructionTimeScale = instructionTimeScale ?? 333333,
            JitMode = jitMode,
            FailOnInvalidOpcode = failOnInvalidOpcode,
            ReloadCfgGraph = reloadCfgGraph,
            EnableSpeculativeCfgExploration = enableSpeculativeCfgExploration,
            RenderingMode = renderingMode,
        };

        _maxCycles = maxCycles;
    }

    public Spice86DependencyInjection Create() {
        _configuration.Umb = EnableUmb;
        Spice86DependencyInjection res = new(_configuration);
        MachineLeakTracker.Track(res.Machine);
        res.Machine.CpuState.Flags.CpuModel = CpuModel.ZET_86;
        // Add a breakpoint after some cycles to ensure no infinite loop can lock the tests
        res.Machine.EmulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_CYCLES, _maxCycles,
            (breakpoint) => Assert.Fail($"Test ran for {((AddressBreakPoint)breakpoint).Address} cycles, something is wrong."), true), true);
        // Init VGA card to map some addresses correctly otherwise there will be errors when saving the ram image
        res.Machine.IoPortDispatcher.WriteByte(Ports.GraphicsControllerAddress, 0x6);
        res.Machine.IoPortDispatcher.WriteByte(Ports.GraphicsControllerData, 0xE);
        return res;
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_ownsExportFolder && Directory.Exists(_exportFolder)) {
            Directory.Delete(_exportFolder, recursive: true);
        }
    }
}