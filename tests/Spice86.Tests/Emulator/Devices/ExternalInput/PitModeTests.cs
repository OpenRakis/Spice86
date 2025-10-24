namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using Xunit;

public class PitModeTests {
    private const ushort PitChannel2Port = 0x42;
    private const ushort PitControlPort = 0x43;
    private readonly IoSystem _ioSystem;
    private readonly DualPic _pic;

    private readonly PitTimer _pit;
    private readonly IPitSpeaker _speaker;

    public PitModeTests() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        _speaker = Substitute.For<IPitSpeaker>();
        State state = new(CpuModel.INTEL_80286);
        var dispatcher = new IOPortDispatcher(new AddressReadWriteBreakpoints(), state, logger, false);
        _ioSystem = new IoSystem(dispatcher, state, logger, false);
        var cpuState = new PicPitCpuState(state) {
            InterruptFlag = true
        };
        _pic = new DualPic(_ioSystem, cpuState, logger);
        _pit = new PitTimer(_ioSystem, _pic, _speaker, logger);
    }

    [Fact]
    public void Mode0_InterruptOnTerminalCount_Behavior() {
        ConfigureChannel2(PitMode.InterruptOnTerminalCount);
        WriteReloadValue(2, 3);

        double gateIndex = _pic.GetFullIndex();
        _pit.SetGate2(true);

        PitChannelSnapshot snapshot = _pit.GetChannelSnapshot(2);
        snapshot.Mode.Should().Be(PitMode.InterruptOnTerminalCount);
        snapshot.Start.Should().Be(gateIndex);
        _speaker.Received(1).SetPitControl(PitMode.InterruptOnTerminalCount);
    }

    [Fact]
    public void Mode1_OneShot_Behavior() {
        ConfigureChannel2(PitMode.OneShot);
        WriteReloadValue(2, 3);

        _pit.SetGate2(true);

        PitChannelSnapshot snapshot = _pit.GetChannelSnapshot(2);
        snapshot.Mode.Should().Be(PitMode.OneShot);
        snapshot.Counting.Should().BeTrue();
        _speaker.Received(1).SetPitControl(PitMode.OneShot);
    }

    [Fact]
    public void Mode2_RateGenerator_Behavior() {
        ConfigureChannel2(PitMode.RateGenerator);
        WriteReloadValue(2, 3);

        _pit.SetGate2(true);
        _pit.SetGate2(false);

        PitChannelSnapshot snapshot = _pit.GetChannelSnapshot(2);
        snapshot.Mode.Should().Be(PitMode.RateGenerator);
        _speaker.Received(1).SetPitControl(PitMode.RateGenerator);
    }

    [Fact]
    public void Mode3_SquareWave_Behavior() {
        ConfigureChannel2(PitMode.SquareWave);
        WriteReloadValue(2, 4);

        _pit.SetGate2(true);
        _pit.SetGate2(false);

        PitChannelSnapshot snapshot = _pit.GetChannelSnapshot(2);
        snapshot.Mode.Should().Be(PitMode.SquareWave);
        _speaker.Received(1).SetPitControl(PitMode.SquareWave);
    }

    [Fact]
    public void Mode4_SoftwareStrobe_Behavior() {
        ConfigureChannel2(PitMode.SoftwareStrobe);
        WriteReloadValue(2, 3);

        _pit.SetGate2(true);

        PitChannelSnapshot snapshot = _pit.GetChannelSnapshot(2);
        snapshot.Mode.Should().Be(PitMode.SoftwareStrobe);
        _speaker.Received(1).SetPitControl(PitMode.SoftwareStrobe);
    }

    private void ConfigureChannel2(PitMode mode) {
        _ioSystem.Write(PitControlPort, GenerateConfigureCounterByte(2, 3, (byte)mode, 0));
    }

    private void WriteReloadValue(byte counterIndex, ushort value) {
        ushort port = counterIndex switch {
            2 => PitChannel2Port,
            _ => throw new ArgumentOutOfRangeException(nameof(counterIndex), counterIndex, null)
        };

        _ioSystem.Write(port, (byte)(value & 0xFF));
        _ioSystem.Write(port, (byte)((value >> 8) & 0xFF));
    }

    private static byte GenerateConfigureCounterByte(byte counter, byte readWritePolicy, byte mode, byte bcd) {
        return (byte)((counter << 6) | (readWritePolicy << 4) | (mode << 1) | bcd);
    }
}