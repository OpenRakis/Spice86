namespace Spice86.Tests;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Shared.Emulator.VM.Breakpoint;

using Xunit;

public class Spice86Creator {
    private readonly Configuration _configuration;
    private readonly long _maxCycles;

    public Spice86Creator(string binName, bool enablePit = false,
        long maxCycles = 100000, bool installInterruptVectors = false, bool failOnUnhandledPort = false, bool enableA20Gate = false,
        bool enableXms = false, bool enableEms = false, string? overrideSupplierClassName = null, string? cDrive = null,
        ushort programEntryPointSegment = 0x170,
        SbType sbType = SbType.None, OplMode oplMode = OplMode.None,
        ushort sbBase = 0x220, byte sbIrq = 7, byte sbDma = 1, byte sbHdma = 5) {
        string executablePath = Path.IsPathRooted(binName) ? binName : $"Resources/cpuTests/{binName}.bin";
        IOverrideSupplier? overrideSupplier = null;
        if (overrideSupplierClassName != null) {
            overrideSupplier = CommandLineParser.ParseFunctionInformationSupplierClassName(overrideSupplierClassName);
        }

        string exportFolder = Path.Join(
            Path.GetTempPath(),
            Guid.NewGuid().ToString()
        );
        _configuration = new Configuration {
            Exe = executablePath,
            // Don't expect any hash for the exe
            ExpectedChecksumValue = [],
            // when false: making sure int8 is not going to be triggered during the tests
            InitializeDOS = installInterruptVectors,
            ProvidedAsmHandlersSegment = 0xF000,
            ProgramEntryPointSegment = programEntryPointSegment,
            TimeMultiplier = enablePit ? 1 : 0,
            //Don"t need nor want to instantiate the UI in emulator unit tests
            HeadlessMode = HeadlessType.Minimal,
            // Use Cycles for proper CpuCycleLimiter + EmulatedClock timing tests for sound
            Cycles = enablePit ? ICyclesLimiter.RealModeCpuCyclesPerMs : null,
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
            RecordedDataDirectory = exportFolder,
            SilencedLogs = true,
        };

        _maxCycles = maxCycles;
    }

    public Spice86DependencyInjection Create() {
        Spice86DependencyInjection res = new(_configuration);
        res.Machine.CpuState.Flags.CpuModel = CpuModel.ZET_86;
        // Add a breakpoint after some cycles to ensure no infinite loop can lock the tests
        res.Machine.EmulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_CYCLES, _maxCycles,
            (breakpoint) => Assert.Fail($"Test ran for {((AddressBreakPoint)breakpoint).Address} cycles, something is wrong."), true), true);
        // Init VGA card to map some addresses correctly otherwise there will be errors when saving the ram image
        res.Machine.IoPortDispatcher.WriteByte(Ports.GraphicsControllerAddress, 0x6);
        res.Machine.IoPortDispatcher.WriteByte(Ports.GraphicsControllerData, 0xE);
        return res;
    }
}




