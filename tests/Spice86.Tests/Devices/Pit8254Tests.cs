namespace Spice86.Tests.Devices;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Shared.Interfaces;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;

using Xunit;

public class Pit8254Tests {
    private readonly Timer _pit;

    public Pit8254Tests() {
        ILoggerService loggerMock = Substitute.For<ILoggerService>();
        State state = new();
        IOPortDispatcher ioPortDispatcher = new(Substitute.For<AddressReadWriteBreakpoints>(), state, loggerMock, true);
        IPauseHandler pauseHandlerMock = Substitute.For<IPauseHandler>();
        Configuration configuration = new();
        CounterConfiguratorFactory counterConfiguratorFactory = new(configuration, state, pauseHandlerMock, loggerMock);
        DualPic dualPic = new(state, ioPortDispatcher, false, false, loggerMock);
        _pit = new Timer(configuration, state, ioPortDispatcher, counterConfiguratorFactory, loggerMock, dualPic);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FreshPitIsAsExpected(byte counterIndex) {
        Pit8254Counter counter = _pit.GetCounter(counterIndex);
        Assert.Equal(18, counter.Activator.Frequency);
        Assert.Equal(3, counter.Mode);
        Assert.Equal(3, counter.ReadWritePolicy);
        Assert.Equal(counterIndex, counter.Index);
        Assert.False(counter.Bcd);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SettingCounterViaCommandPort(byte counterIndex) {
        // Act
        _pit.WriteByte(0x43, GenerateConfigureCounterByte(counterIndex, 1, 2, 1));

        // Assert
        Pit8254Counter counter = _pit.GetCounter(counterIndex);
        Assert.Equal(1, counter.ReadWritePolicy);
        Assert.Equal(2, counter.Mode);
        Assert.True(counter.Bcd);
    }

    [Theory]
    // Write values of 01, should only write on LSB for readWritePolicy 1
    [InlineData(0x40, 0, 1, 0x01, 0x01, 0xFF01)]
    [InlineData(0x41, 1, 1, 0x01, 0x01, 0xFF01)]
    [InlineData(0x42, 2, 1, 0x01, 0x01, 0xFF01)]
    // Write values of 01, should only write on MSB for readWritePolicy 2
    [InlineData(0x40, 0, 2, 0x01, 0x01, 0x01EE)]
    [InlineData(0x41, 1, 2, 0x01, 0x01, 0x01EE)]
    [InlineData(0x42, 2, 2, 0x01, 0x01, 0x01EE)]
    // Write values of 0102, should write on full word for readWritePolicy 3
    [InlineData(0x40, 0, 3, 0x01, 0x02, 0x0201)]
    [InlineData(0x41, 1, 3, 0x01, 0x02, 0x0201)]
    [InlineData(0x42, 2, 3, 0x01, 0x02, 0x0201)]
    public void ReadWritePolicyWritesReloadValueAsExpected(ushort port, byte counterIndex, byte readWritePolicy, byte writeValue1, byte writeValue2, ushort expectedValue) {
        // Arrange
        Pit8254Counter counter = _pit.GetCounter(counterIndex);
        counter.ReloadValue = 0xFFEE;
        _pit.WriteByte(0x43, GenerateConfigureCounterByte(counterIndex, readWritePolicy, 3, 0));

        // Act 
        _pit.WriteByte(port, writeValue1);
        _pit.WriteByte(port, writeValue2);

        // Assert
        Assert.Equal(expectedValue, _pit.GetCounter(counterIndex).ReloadValue);
    }

    [Theory]
    // Write values of 01, should only read on LSB for readWritePolicy 1
    [InlineData(0x40, 0, 1, 0x01, 0x01)]
    [InlineData(0x41, 1, 1, 0x01, 0x01)]
    [InlineData(0x42, 2, 1, 0x01, 0x01)]
    // Write values of 01, should only read on MSB for readWritePolicy 2
    [InlineData(0x40, 0, 2, 0x02, 0x02)]
    [InlineData(0x41, 1, 2, 0x02, 0x02)]
    [InlineData(0x42, 2, 2, 0x02, 0x02)]
    // Write values of 0102, should read LSB then MSB for readWritePolicy 3
    [InlineData(0x40, 0, 3, 0x01, 0x02)]
    [InlineData(0x41, 1, 3, 0x01, 0x02)]
    [InlineData(0x42, 2, 3, 0x01, 0x02)]
    public void ReadWritePolicyReadCurrentCountAsExpected(ushort port, byte counterIndex, byte readWritePolicy, byte readValue1, byte readValue2) {
        // Arrange
        _pit.GetCounter(counterIndex).CurrentCount = 0x0201;
        _pit.WriteByte(0x43, GenerateConfigureCounterByte(counterIndex, readWritePolicy, 3, 0));

        // Act
        byte read1 = _pit.ReadByte(port);
        byte read2 = _pit.ReadByte(port);

        // Assert
        Assert.Equal(readValue1, read1);
        Assert.Equal(readValue2, read2);
    }

    [Theory]
    [InlineData(0x40, 0)]
    [InlineData(0x41, 1)]
    [InlineData(0x42, 2)]
    public void ReadWritePolicy3PartialWriteNoEffect(ushort port, byte counterIndex) {
        // Arrange
        Pit8254Counter counter = _pit.GetCounter(counterIndex);
        counter.ReloadValue = 0x0000;

        // Act
        _pit.WriteByte(port, 0x01);

        // Assert (until full value has been written, value should not be updated)
        Assert.Equal(0x0000, counter.ReloadValue);
    }

    [Theory]
    [InlineData(0x40, 0)]
    [InlineData(0x41, 1)]
    [InlineData(0x42, 2)]
    public void LatchModeTest(ushort port, byte counterIndex) {
        // Arrange
        Pit8254Counter counter = _pit.GetCounter(counterIndex);
        counter.CurrentCount = 0x1234;
        // rw policy 0 means latch (current count is kept until read)
        _pit.WriteByte(0x43, GenerateConfigureCounterByte(counterIndex, 0, 3, 0));
        counter.CurrentCount = 0x5678;
        // a second latch command should not modify the latch value until it is read fully
        _pit.WriteByte(0x43, GenerateConfigureCounterByte(counterIndex, 0, 3, 0));

        // Act
        byte lsbLatch = _pit.ReadByte(port);
        byte msbLatch = _pit.ReadByte(port);
        byte lsb = _pit.ReadByte(port);
        byte msb = _pit.ReadByte(port);

        // Assert
        // first read was the latched value
        Assert.Equal(0x34, lsbLatch);
        Assert.Equal(0x12, msbLatch);
        // second read is the actual value
        Assert.Equal(0x78, lsb);
        Assert.Equal(0x56, msb);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SettingCounterReloadValue0ResultsInCorrectFrequency(byte counterIndex) {
        Pit8254Counter counter = _pit.GetCounter(counterIndex);
        // 0 is equivalent to a value of 0x10000 (FFFF+1)
        counter.ReloadValue = 0;
        Assert.Equal(18, counter.Activator.Frequency);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SettingCounterReloadValueToNonZeroResultsInCorrectFrequency(byte counterIndex) {
        Pit8254Counter counter = _pit.GetCounter(counterIndex);
        // 1000 Hz
        counter.ReloadValue = 1193182 / 1000;
        Assert.Equal(1000, counter.Activator.Frequency);
    }

    private byte GenerateConfigureCounterByte(byte counter, byte readWritePolicy, byte mode, byte bcd) {
        return (byte)(counter << 6 | readWritePolicy << 4 | mode << 1 | bcd);
    }
}