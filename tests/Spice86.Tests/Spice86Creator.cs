namespace Spice86.Tests;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using System;

using Xunit;

public class Spice86Creator {
    private readonly Configuration _configuration;
    private readonly long _maxCycles;

    public Spice86Creator(string binName, bool enableCfgCpu, bool enablePit = false, bool recordData = false,
        long maxCycles = 100000, bool failOnUnhandledPort = false) {
        _configuration = new Configuration {
            Exe = $"Resources/cpuTests/{binName}.bin",
            // Don't expect any hash for the exe
            ExpectedChecksumValue = Array.Empty<byte>(),
            // making sure int8 is not going to be triggered during the tests
            InitializeDOS = false,
            DumpDataOnExit = recordData,
            TimeMultiplier = enablePit ? 1 : 0,
            //Don"t need nor want to instantiate the UI in emulator unit tests
            HeadlessMode = true,
            // Use instructions per second based timer for predictability if timer is enabled
            InstructionsPerSecond = enablePit ? 100000 : null,
            CfgCpu = enableCfgCpu,
            AudioEngine = AudioEngine.Dummy,
            FailOnUnhandledPort = failOnUnhandledPort
        };
        _maxCycles = maxCycles;
    }

    public Spice86DependencyInjection Create() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        Spice86DependencyInjection res = new(loggerService, _configuration);
        res.Machine.Cpu.ErrorOnUninitializedInterruptHandler = false;
        res.Machine.CpuState.Flags.IsDOSBoxCompatible = false;
        // Add a breakpoint after some cycles to ensure no infinite loop can lock the tests
        res.Machine.EmulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_CYCLES, _maxCycles,
            (breakpoint) => Assert.Fail($"Test ran for {((AddressBreakPoint)breakpoint).Address} cycles, something is wrong."), true), true);
        return res;
    }
}