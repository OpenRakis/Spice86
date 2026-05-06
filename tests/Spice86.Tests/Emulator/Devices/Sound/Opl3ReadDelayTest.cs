namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Shared.Interfaces;

using System;

using Xunit;

[Trait("Category", "Sound")]
public class Opl3ReadDelayTest {

    [Fact]
    public void Opl3ReadDelayIsAMicroSecondAndAHalf() {
        //Arrange
        State state = new(CpuModel.INTEL_80386);
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        CyclesClock clock = new CyclesClock(state, 3000, null, DateTimeOffset.Now);
        Opl3Fm opl3fm = new(new(OplMode.Opl3, 0x220, SbMixer: true),
            new SoftwareMixer(Audio.Filters.AudioEngine.Dummy,
            new PauseHandler(loggerService)),
            state,
            clock,
            new Core.Emulator.IOPorts.IOPortDispatcher(new(), state, loggerService, false),
            false,
            loggerService);
        state.Cycles = 100;

        //Act
        double before = clock.ElapsedTimeMs;
        _ = opl3fm.ReadByte(0x330);
        double after = clock.ElapsedTimeMs;

        //Assert
        double diff = after - before;
        diff.Should().BeApproximately(1.5, 1);
    }
}
