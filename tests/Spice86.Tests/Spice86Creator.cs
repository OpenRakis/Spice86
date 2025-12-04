namespace Spice86.Tests;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Core.Emulator.VM.CycleBudget;
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
        int staticCycleBudget = GetStaticCycleBudget(instructionsPerSecond);

        _configuration = new Configuration {
            Exe = Path.IsPathRooted(binName) ? binName : $"Resources/cpuTests/{binName}.bin",
            // Don't expect any hash for the exe
            ExpectedChecksumValue = [],
            // when false: making sure int8 is not going to be triggered during the tests
            InitializeDOS = installInterruptVectors,
            ProvidedAsmHandlersSegment = 0xF000,
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
            CyclesBudgeter = new StaticCyclesBudgeter(staticCycleBudget),
            // Use provided segment or default (0x0070 for DOS tests to avoid wraparound at 1MB)
            ProgramEntryPointSegment = programEntryPointSegment ?? 0x0070
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

    private static int GetStaticCycleBudget(int? instructionsPerSecond) {
        int candidateCyclesPerMs = instructionsPerSecond.HasValue
            ? (int)Math.Round(instructionsPerSecond.Value / 1000.0)
            : ICyclesLimiter.RealModeCpuCyclesPerMs;
        CpuCycleLimiter limiter = new(candidateCyclesPerMs);
        return limiter.TargetCpuCyclesPerMs;
    }
}