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
        bool enableXms = false, bool enableEms = false, string? overrideSupplierClassName = null, long? instructionsPerSecond = null) {
        IOverrideSupplier? overrideSupplier = null;
        if (overrideSupplierClassName != null) {
            CommandLineParser parser = new();
            overrideSupplier = parser.ParseCommandLine(["--OverrideSupplierClassName", overrideSupplierClassName])?.OverrideSupplier;
        }

        long? effectiveInstructionsPerSecond = instructionsPerSecond ?? (enablePit ? 100000 : null);
        int staticCycleBudget = GetStaticCycleBudget(effectiveInstructionsPerSecond);

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
            InstructionsPerSecond = effectiveInstructionsPerSecond,
            CfgCpu = enableCfgCpu,
            AudioEngine = AudioEngine.Dummy,
            FailOnUnhandledPort = failOnUnhandledPort,
            A20Gate = enableA20Gate,
            OverrideSupplier = overrideSupplier,
            Xms = enableXms,
            Ems = enableEms,
            CyclesBudgeter = new StaticCyclesBudgeter(staticCycleBudget)
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

    private static int GetStaticCycleBudget(long? instructionsPerSecond) {
        int candidateCyclesPerMs = instructionsPerSecond.HasValue
            ? (int)Math.Round(instructionsPerSecond.Value / 1000.0)
            : ICyclesLimiter.RealModeCpuCyclesPerMs;
        // For very high IPS values (performance tests), don't apply the limiter's cap
        if (candidateCyclesPerMs > 60000) {
            Console.WriteLine($"Using uncapped cycles budget: {candidateCyclesPerMs} cycles/ms (from IPS: {instructionsPerSecond})");
            return candidateCyclesPerMs;
        }
        CpuCycleLimiter limiter = new(candidateCyclesPerMs);
        Console.WriteLine($"Using capped cycles budget: {limiter.TargetCpuCyclesPerMs} cycles/ms (from IPS: {instructionsPerSecond})");
        return limiter.TargetCpuCyclesPerMs;
    }
}
