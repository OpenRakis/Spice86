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

    [Fact]
    public void AudioCallbackUsesRawShortAmplitude() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        AddressReadWriteBreakpoints breakpoints = new();
        State state = new(CpuModel.INTEL_80286);
        IOPortDispatcher dispatcher = new(breakpoints, state, loggerService, failOnUnhandledPort: false);
        using Mixer mixer = new(loggerService, AudioEngine.Dummy);
        EmulatedClock clock = new();
        EmulationLoopScheduler scheduler = new(clock, loggerService);
        DualPic dualPic = new(dispatcher, state, loggerService, false);

        short sample = 12000;
        void Generator(Span<short> buffer) {
            for (int i = 0; i < buffer.Length; i += 2) {
                buffer[i] = sample;
                buffer[i + 1] = (short)-sample;
            }
        }

        using Opl3Fm opl3 = new(mixer, state, dispatcher, false, loggerService, scheduler, clock, dualPic,
            useAdlibGold: false, enableOplIrq: false, sampleGenerator: Generator);

        opl3.AudioCallback(4);

        opl3.MixerChannel.AudioFrames.Should().NotBeEmpty();
        opl3.MixerChannel.AudioFrames[0].Left.Should().Be(sample);
        opl3.MixerChannel.AudioFrames[0].Right.Should().Be(-sample);
    }
}
