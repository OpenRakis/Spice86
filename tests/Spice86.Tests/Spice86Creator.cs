namespace Spice86.Tests;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Shared.Emulator.VM.Breakpoint;

using Xunit;

public class Spice86Creator {
    private readonly Configuration _configuration;
    private readonly long _maxCycles;

    public Spice86Creator(string binName, bool enableCfgCpu, bool enablePit = false, bool recordData = false,
        long maxCycles = 100000, bool installInterruptVectors = false, bool failOnUnhandledPort = false, bool enableA20Gate = false,
        bool enableXms = false, bool enableEms = false, string? overrideSupplierClassName = null, ushort? programEntryPointSegment = null) {
        IOverrideSupplier? overrideSupplier = null;
        if (overrideSupplierClassName != null) {
            CommandLineParser parser = new();
            overrideSupplier = parser.ParseCommandLine(["--OverrideSupplierClassName", overrideSupplierClassName])?.OverrideSupplier;
        }

        int? instructionsPerSecond = enablePit ? 100000 : null;

        _configuration = new Configuration {
            Exe = Path.IsPathRooted(binName) ? binName : $"Resources/cpuTests/{binName}.bin",
            // Don't expect any hash for the exe
            ExpectedChecksumValue = [],
            // when false: making sure int8 is not going to be triggered during the tests
            InitializeDOS = installInterruptVectors,
            ProvidedAsmHandlersSegment = 0xF000,
            // Default to 0x170 to avoid erasing IVT (0x0000-0x03FF), can be overridden
            ProgramEntryPointSegment = programEntryPointSegment ?? 0x170,
            DumpDataOnExit = recordData,
            TimeMultiplier = enablePit ? 1 : 0,
            //Don"t need nor want to instantiate the UI in emulator unit tests
            HeadlessMode = HeadlessType.Minimal,
            // Use instructions per second based timer for predictability if timer is enabled
            InstructionsPerSecond = instructionsPerSecond,
            CfgCpu = enableCfgCpu,
            AudioEngine = AudioEngine.Dummy,
            FailOnUnhandledPort = failOnUnhandledPort,
            A20Gate = enableA20Gate,
            OverrideSupplier = overrideSupplier,
            Xms = enableXms,
            Ems = enableEms,
        };

        _maxCycles = maxCycles;
    }

    public Spice86DependencyInjection Create() {
        Spice86DependencyInjection res = new(_configuration);
        res.Machine.CpuState.Flags.CpuModel = CpuModel.ZET_86;
        // Add a breakpoint after some cycles to ensure no infinite loop can lock the tests
        res.Machine.EmulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_CYCLES, _maxCycles,
            (breakpoint) => Assert.Fail($"Test ran for {((AddressBreakPoint)breakpoint).Address} cycles, something is wrong."), true), true);
        return res;
    }
}
