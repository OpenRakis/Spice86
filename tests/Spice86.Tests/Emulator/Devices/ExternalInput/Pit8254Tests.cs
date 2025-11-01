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

public class Pit8254Tests {
    private const ushort PitChannel0Port = 0x40;
    private const ushort PitChannel1Port = 0x41;
    private const ushort PitChannel2Port = 0x42;
    private const ushort PitControlPort = 0x43;
    private readonly IoSystem _ioSystem;

    private readonly PitTimer _pit;
    private readonly IPitSpeaker _speaker;

    public Pit8254Tests() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        _speaker = Substitute.For<IPitSpeaker>();
        State state = new(CpuModel.INTEL_80286);
        var dispatcher = new IOPortDispatcher(new AddressReadWriteBreakpoints(), state, logger, false);
        _ioSystem = new IoSystem(dispatcher, state, logger, false);
        var cpuState = new PicPitCpuState(state) {
            InterruptFlag = true
        };
        var pic = new DualPic(_ioSystem, cpuState, logger);
        _pit = new PitTimer(_ioSystem, pic, _speaker, logger);
    }

    [Theory]
    [InlineData(0, PitMode.SquareWave, 0x10000)]
    [InlineData(1, PitMode.RateGenerator, 18)]
    [InlineData(2, PitMode.SquareWave, 1320)]
    public void FreshPitIsAsExpected(byte counterIndex, PitMode expectedMode, int expectedCount) {
        PitChannelSnapshot snapshot = _pit.GetChannelSnapshot(counterIndex);

        snapshot.Mode.Should().Be(expectedMode);
        snapshot.Bcd.Should().BeFalse();
        snapshot.Count.Should().Be(expectedCount);
    }

    [Theory]
    [InlineData(0, 1, PitMode.RateGenerator, true)]
    [InlineData(1, 2, PitMode.SquareWave, false)]
    [InlineData(2, 3, PitMode.SoftwareStrobe, false)]
    public void SettingCounterViaCommandPort(byte counterIndex, byte readWritePolicy, PitMode expectedMode,
        bool expectedBcd) {
        ConfigureCounter(counterIndex, readWritePolicy, (byte)expectedMode, expectedBcd ? (byte)1 : (byte)0);

        PitChannelSnapshot snapshot = _pit.GetChannelSnapshot(counterIndex);

        snapshot.Mode.Should().Be(expectedMode);
        snapshot.Bcd.Should().Be(expectedBcd);
    }

    [Theory]
    [InlineData(PitChannel0Port, (byte)0)]
    [InlineData(PitChannel2Port, (byte)2)]
    public void ReadWritePolicy3PartialWriteNoEffect(ushort port, byte counterIndex) {
        WriteFullReload(counterIndex, 0x0000);
        PitChannelSnapshot snapshotBefore = _pit.GetChannelSnapshot(counterIndex);

        ConfigureCounter(counterIndex, 3, 3, 0);
        _ioSystem.Write(port, 0x01);

        PitChannelSnapshot snapshotAfter = _pit.GetChannelSnapshot(counterIndex);
        snapshotAfter.Count.Should().Be(snapshotBefore.Count);
    }

    [Theory]
    [InlineData(0, (ushort)0x0200)]
    [InlineData(2, (ushort)0x2000)]
    public void ReloadValueIsReflectedInSnapshotAndLatch(byte counterIndex, ushort reload) {
        _speaker.ClearReceivedCalls();
        WriteFullReload(counterIndex, reload);
        if (counterIndex == 2) {
            _pit.SetGate2(true);
        }

        int expectedCount = reload != 0 ? reload : 0x10000;
        PitChannelSnapshot snapshot = _pit.GetChannelSnapshot(counterIndex);
        snapshot.Count.Should().Be(expectedCount);

        ushort latched = ReadLatchedValue(counterIndex);
        ushort expectedLatched = reload == 0 ? (ushort)0x0000 : reload;
        latched.Should().Be(expectedLatched);

        if (counterIndex == 2) {
            _speaker.Received().SetCounter(expectedCount, PitMode.SquareWave);
            _speaker.ClearReceivedCalls();
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    public void ZeroReloadMatchesExpectedFrequency(byte counterIndex, ushort reload) {
        WriteFullReload(counterIndex, reload);
        if (counterIndex == 2) {
            _pit.SetGate2(true);
        }

        PitChannelSnapshot snapshot = _pit.GetChannelSnapshot(counterIndex);
        double actual = ComputeFrequency(snapshot);
        double expected = ComputeExpectedFrequency(reload);
        actual.Should().BeApproximately(expected, 0.05);
    }

    [Theory]
    [InlineData(0, (ushort)1000)]
    [InlineData(2, (ushort)2000)]
    public void ReloadValueProducesExpectedFrequency(byte counterIndex, ushort reload) {
        WriteFullReload(counterIndex, reload);
        if (counterIndex == 2) {
            _pit.SetGate2(true);
        }

        PitChannelSnapshot snapshot = _pit.GetChannelSnapshot(counterIndex);
        double actual = ComputeFrequency(snapshot);
        double expected = ComputeExpectedFrequency(reload);
        actual.Should().BeApproximately(expected, 0.05);
    }

    private void ConfigureCounter(byte counter, byte readWritePolicy, byte mode, byte bcd) {
        _ioSystem.Write(PitControlPort, GenerateConfigureCounterByte(counter, readWritePolicy, mode, bcd));
    }

    private void WriteFullReload(byte counterIndex, ushort value) {
        ConfigureCounter(counterIndex, 3, 3, 0);
        ushort port = GetPort(counterIndex);
        _ioSystem.Write(port, (byte)(value & 0xFF));
        _ioSystem.Write(port, (byte)((value >> 8) & 0xFF));
    }

    private static ushort GetPort(byte counterIndex) {
        return counterIndex switch {
            0 => PitChannel0Port,
            1 => PitChannel1Port,
            2 => PitChannel2Port,
            _ => throw new ArgumentOutOfRangeException(nameof(counterIndex), counterIndex, null)
        };
    }

    private static double ComputeFrequency(PitChannelSnapshot snapshot) {
        if (snapshot.Delay <= 0) {
            return double.PositiveInfinity;
        }

        return 1000.0 / snapshot.Delay;
    }

    private static double ComputeExpectedFrequency(ushort reload) {
        double divisor = reload != 0 ? reload : 65536.0;
        return PitTimer.PitTickRate / divisor;
    }

    private ushort ReadLatchedValue(byte counterIndex) {
        _ioSystem.Write(PitControlPort, GenerateConfigureCounterByte(counterIndex, 0, 0, 0));
        ushort port = GetPort(counterIndex);
        byte lsb = (byte)_ioSystem.Read(port);
        byte msb = (byte)_ioSystem.Read(port);
        return (ushort)(lsb | (msb << 8));
    }

    private static byte GenerateConfigureCounterByte(byte counter, byte readWritePolicy, byte mode, byte bcd) {
        return (byte)((counter << 6) | (readWritePolicy << 4) | (mode << 1) | bcd);
    }
}