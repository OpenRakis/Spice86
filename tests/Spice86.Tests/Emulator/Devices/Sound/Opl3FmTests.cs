namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Libs.Sound.Devices.NukedOpl3;
using Spice86.Shared.Interfaces;

using Xunit;

public class Opl3FmTests {
    [Fact]
    public void AdlibGoldPortsAreNotRegisteredByDefault() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        AddressReadWriteBreakpoints breakpoints = new();
        State state = new(CpuModel.INTEL_80286);
        IOPortDispatcher dispatcher = new(breakpoints, state, loggerService, failOnUnhandledPort: true);
        using Mixer mixer = new(loggerService, AudioEngine.Dummy);
        EmulatedClock clock = new();
        EmulationLoopScheduler scheduler = new(clock, loggerService);
        DualPic dualPic = new(dispatcher, state, loggerService, true);

        using Opl3Fm opl3 = new(mixer, state, dispatcher, true, loggerService, scheduler, clock, dualPic,
            useAdlibGold: false, enableOplIrq: false);

        opl3.IsAdlibGoldEnabled.Should().BeFalse();
        Action read = () => dispatcher.ReadByte(IOplPort.AdLibGoldAddressPortNumber);
        read.Should().Throw<UnhandledIOPortException>();
    }

    [Fact]
    public void AdlibGoldPortsAreRegisteredWhenEnabled() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        AddressReadWriteBreakpoints breakpoints = new();
        State state = new(CpuModel.INTEL_80286);
        IOPortDispatcher dispatcher = new(breakpoints, state, loggerService, failOnUnhandledPort: true);
        using Mixer mixer = new(loggerService, AudioEngine.Dummy);
        EmulatedClock clock = new();
        EmulationLoopScheduler scheduler = new(clock, loggerService);
        DualPic dualPic = new(dispatcher, state, loggerService, true);

        using Opl3Fm opl3 = new(mixer, state, dispatcher, true, loggerService, scheduler, clock, dualPic,
            useAdlibGold: true, enableOplIrq: false);

        opl3.IsAdlibGoldEnabled.Should().BeTrue();
        Action read = () => dispatcher.ReadByte(IOplPort.AdLibGoldAddressPortNumber);
        read.Should().NotThrow();
    }
}
