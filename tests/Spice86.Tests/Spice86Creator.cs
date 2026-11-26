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
    /// <summary>Environment variable that asks every discovery run to copy its standard dump (the
    /// listing and the program files of the generated C#) into <c>&lt;value&gt;/&lt;golden key&gt;/</c>
    /// before the run's export folder is deleted. Used by the generated-code diff workflow.</summary>
    internal const string DumpsFolderEnvironmentVariable = "SPICE86_DUMPS_FOLDER";

    private readonly Configuration _configuration;
    private readonly long _maxCycles;
    private readonly string _exportFolder;
    private readonly bool _ownsExportFolder;
    private readonly AsmFixtures.AsmRunSettings? _settings;

    public bool EnableUmb { get; set; } = true;

    /// <summary>The program load segment every fixture run is configured with unless a caller
    /// passes a different one.</summary>
    internal const ushort ProgramEntryPointSegment = 0x170;

    public bool HltStopsEmulation { get; set; } = true;

    public Spice86Creator(string binName, bool enablePit = false,
        long maxCycles = 100000, bool installInterruptVectors = false, bool failOnUnhandledPort = false, bool enableA20Gate = false,
        bool enableXms = false, bool enableEms = false, string? overrideSupplierClassName = null, string? cDrive = null,
        IOverrideSupplier? overrideSupplier = null,
        ushort programEntryPointSegment = ProgramEntryPointSegment,
        SbType sbType = SbType.None, OplMode oplMode = OplMode.None,
        ushort sbBase = 0x220, byte sbIrq = 7, byte sbDma = 1, byte sbHdma = 5,
        string? exeArgs = null, long? instructionTimeScale = null,
        JitMode jitMode = JitMode.InterpretedOnly, bool failOnInvalidOpcode = false,
        string? recordedDataDirectory = null, bool reloadCfgGraph = false,
        bool enableSpeculativeCfgExploration = true, DateTimeOffset? clockStartTime = null,
        bool generatedCodeCheckBuild = false)
        : this(binName, enablePit, maxCycles,
            installInterruptVectors, failOnUnhandledPort, enableA20Gate, enableXms, enableEms,
            overrideSupplierClassName, cDrive, overrideSupplier, programEntryPointSegment, sbType, oplMode,
            sbBase, sbIrq, sbDma, sbHdma, exeArgs, instructionTimeScale, jitMode, failOnInvalidOpcode,
            recordedDataDirectory, reloadCfgGraph, RenderingMode.Async, 0xF000, null,
            enableSpeculativeCfgExploration, clockStartTime, generatedCodeCheckBuild) {
    }

    internal Spice86Creator(AsmFixtures.AsmRunSettings settings, JitMode jitMode, IOverrideSupplier? overrideSupplier)
        : this(settings.BinName, settings.EnablePit, settings.MaxCycles, settings.InstallInterruptVectors,
            settings.FailOnUnhandledPort, settings.EnableA20Gate, settings.EnableXms, settings.EnableEms,
            null, settings.CDrive, overrideSupplier,
            ProgramEntryPointSegment, settings.SbType, settings.OplMode, 0x220, 7, 1, 5,
            settings.ExeArgs, settings.InstructionTimeScale, jitMode, false,
            settings.RecordedDataDirectory, settings.ReloadCfgGraph,
            settings.RenderingMode, settings.AsmHandlersSegment, settings.CpuModel,
            settings.EnableSpeculativeCfgExploration, TestClockStartTime,
            settings.GeneratedCodeCheckBuild) {
        EnableUmb = settings.EnableUmb;
        HltStopsEmulation = settings.HltStopsEmulation;
        _settings = settings;
    }

    /// <summary>The fixed emulated start time of fixture runs: every machine reports the same DOS
    /// date and time, so timestamps written to emulated RAM are reproducible between runs.</summary>
    private static readonly DateTimeOffset TestClockStartTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private Spice86Creator(string binName, bool enablePit, long maxCycles, bool installInterruptVectors,
        bool failOnUnhandledPort, bool enableA20Gate, bool enableXms, bool enableEms,
        string? overrideSupplierClassName, string? cDrive, IOverrideSupplier? overrideSupplier,
        ushort programEntryPointSegment, SbType sbType, OplMode oplMode, ushort sbBase, byte sbIrq,
        byte sbDma, byte sbHdma, string? exeArgs, long? instructionTimeScale, JitMode jitMode,
        bool failOnInvalidOpcode, string? recordedDataDirectory, bool reloadCfgGraph,
        RenderingMode renderingMode, ushort asmHandlersSegment, CpuModel? cpuModel,
        bool enableSpeculativeCfgExploration, DateTimeOffset? clockStartTime,
        bool generatedCodeCheckBuild) {
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
            CpuModel = cpuModel ?? CpuModel.ZET_86,
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
            JitGeneratorIr = TestJitSettings.GeneratorIr,
            FailOnInvalidOpcode = failOnInvalidOpcode,
            ReloadCfgGraph = reloadCfgGraph,
            RenderingMode = renderingMode,
            EnableSpeculativeCfgExploration = enableSpeculativeCfgExploration,
            ClockStartTime = clockStartTime,
            GeneratedCodeCheckBuild = generatedCodeCheckBuild,
        };

        _maxCycles = maxCycles;
    }

    public Spice86DependencyInjection Create() {
        _configuration.Umb = EnableUmb;
        // Test programs end with HLT, often with interrupts enabled.
        _configuration.HltStopsEmulation = HltStopsEmulation;
        Spice86DependencyInjection res = new(_configuration);
        MachineLeakTracker.Track(res.Machine);
        res.Machine.CpuState.Flags.CpuModel = CpuModel.ZET_86;
        // Add a breakpoint after some cycles to ensure no infinite loop can lock the tests
        res.Machine.EmulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_CYCLES, _maxCycles,
            (breakpoint, _) => Assert.Fail($"Test ran for {((AddressBreakPoint)breakpoint).Address} cycles, something is wrong."), true), true);
        // Init VGA card to map some addresses correctly otherwise there will be errors when saving the ram image
        res.Machine.IoPortDispatcher.WriteByte(Ports.GraphicsControllerAddress, 0x6);
        res.Machine.IoPortDispatcher.WriteByte(Ports.GraphicsControllerData, 0xE);
        return res;
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_configuration.OverrideSupplier is null) {
            GeneratedCodeCoverageReport.RecordIfEnabled(_exportFolder, _configuration.Exe);
        }
        CopyDumpIfEnabled();
        if (_ownsExportFolder && Directory.Exists(_exportFolder)) {
            Directory.Delete(_exportFolder, recursive: true);
        }
    }

    /// <summary>
    /// When <see cref="DumpsFolderEnvironmentVariable"/> is set and this run is a discovery run (no
    /// override supplier, interpreted JIT), copies the listing and the program files of the generated
    /// C# into <c>&lt;folder&gt;/&lt;golden key&gt;/</c>. The copy is built in a sibling temporary
    /// directory and moved into place; when the target already exists the copies are compared file by
    /// file and the new one deleted, and a difference means two runs of one key produced different
    /// dumps.
    /// </summary>
    private void CopyDumpIfEnabled() {
        string? dumpsFolder = Environment.GetEnvironmentVariable(DumpsFolderEnvironmentVariable);
        if (dumpsFolder == null || _configuration.OverrideSupplier != null
            || _configuration.JitMode != JitMode.InterpretedOnly || !Directory.Exists(_exportFolder)
            || _settings is not { } settings) {
            return;
        }
        string goldenKey = MachineGoldens.GoldenKey(settings);
        string targetDirectory = Path.Join(dumpsFolder, goldenKey);
        string stagingDirectory = Path.Join(dumpsFolder, "." + goldenKey + "." + Guid.NewGuid().ToString("N"));
        StageDump(stagingDirectory, settings);
        if (!Directory.Exists(targetDirectory)) {
            Directory.Move(stagingDirectory, targetDirectory);
            return;
        }
        string? difference = FirstDifference(stagingDirectory, targetDirectory);
        Directory.Delete(stagingDirectory, recursive: true);
        if (difference != null) {
            throw new InvalidOperationException(
                $"Two runs of '{goldenKey}' produced different dumps: {difference}");
        }
    }

    private void StageDump(string stagingDirectory, AsmFixtures.AsmRunSettings settings) {
        // The dump lives in a program-hash subdirectory of the export folder
        // (EmulatorStateSerializationFolderFactory).
        string dumpRoot = Directory.GetDirectories(_exportFolder)
            .FirstOrDefault(directory => Directory.GetFiles(directory, "spice86dump*").Length > 0)
            ?? _exportFolder;
        Directory.CreateDirectory(stagingDirectory);
        string listingPath = Path.Join(dumpRoot, "spice86dumpListing.asm");
        if (File.Exists(listingPath)) {
            string emulatorSegmentPrefix = settings.AsmHandlersSegment.ToString("X4") + ":";
            File.WriteAllLines(Path.Join(stagingDirectory, "spice86dumpListing.asm"),
                File.ReadLines(listingPath).Where(line => !line.StartsWith(emulatorSegmentPrefix, StringComparison.Ordinal)));
        }
        string generatedDirectory = Path.Join(dumpRoot, "spice86dumpCfgGeneratedOverrides");
        if (!Directory.Exists(generatedDirectory)) {
            return;
        }
        foreach (string file in Directory.GetFiles(generatedDirectory, "*.cs")) {
            string name = Path.GetFileName(file);
            if (AsmFixtures.GeneratedCodeChecks.IsProgramFile(name)) {
                File.Copy(file, Path.Join(stagingDirectory, name));
            }
        }
    }

    private static string? FirstDifference(string leftDirectory, string rightDirectory) {
        string[] leftFiles = Directory.GetFiles(leftDirectory).Select(Path.GetFileName).Order().ToArray()!;
        string[] rightFiles = Directory.GetFiles(rightDirectory).Select(Path.GetFileName).Order().ToArray()!;
        if (!leftFiles.SequenceEqual(rightFiles)) {
            return $"file sets differ ({string.Join(',', leftFiles)} vs {string.Join(',', rightFiles)})";
        }
        foreach (string name in leftFiles) {
            if (!File.ReadAllBytes(Path.Join(leftDirectory, name))
                    .SequenceEqual(File.ReadAllBytes(Path.Join(rightDirectory, name)))) {
                return $"file {name} differs";
            }
        }
        return null;
    }
}