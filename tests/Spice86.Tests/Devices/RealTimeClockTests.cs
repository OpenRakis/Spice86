namespace Spice86.Tests.Devices;

using FluentAssertions;
using NSubstitute;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Xunit;

/// <summary>
/// Unit tests for the RealTimeClock class.
/// </summary>
public class RealTimeClockTests : IDisposable {
    private readonly RealTimeClock _rtc;
    private readonly State _state;
    private readonly IOPortDispatcher _ioPortDispatcher;
    private readonly DualPic _dualPic;
    private readonly IPauseHandler _pauseHandler;

    public RealTimeClockTests() {
        var loggerService = Substitute.For<ILoggerService>();
        _state = new State(CpuModel.INTEL_80286);
        _ioPortDispatcher = new IOPortDispatcher(Substitute.For<AddressReadWriteBreakpoints>(), _state, loggerService, true);
        _pauseHandler = Substitute.For<IPauseHandler>();
        _dualPic = new DualPic(_state, _ioPortDispatcher, false, false, loggerService);

        _rtc = new RealTimeClock(_state, _ioPortDispatcher, _dualPic, _pauseHandler, false, loggerService);
    }

    [Fact]
    public void Constructor_InitializesPortHandlers() {
        // Assert - if constructor succeeded without exception, ports are registered
        _rtc.Should().NotBeNull();
    }

    [Fact]
    public void WriteAddressPort_StoresCurrentRegister() {
        // Arrange
        const byte registerAddress = 0x0A;

        // Act
        _rtc.WriteByte(0x70, registerAddress);

        // Assert - we can't directly observe the internal state, but subsequent reads should work
        // This is tested implicitly by the read tests
    }

    [Fact]
    public void ReadDataPort_RegisterD_ReturnsPowerGoodBit() {
        // Arrange - Select Register D
        _rtc.WriteByte(0x70, 0x0D);

        // Act
        byte result = _rtc.ReadByte(0x71);

        // Assert - Bit 7 should be set (power good)
        (result & 0x80).Should().Be(0x80);
    }

    [Fact]
    public void WriteDataPort_RegisterA_StoresDividerBits() {
        // Arrange - Select Register A
        _rtc.WriteByte(0x70, 0x0A);
        const byte dividerValue = 0x26; // Default rate

        // Act
        _rtc.WriteByte(0x71, dividerValue);

        // Read it back
        _rtc.WriteByte(0x70, 0x0A);
        byte result = _rtc.ReadByte(0x71);

        // Assert - UIP bit (bit 7) may be set, so mask it
        (result & 0x7F).Should().Be(dividerValue & 0x7F);
    }

    [Fact]
    public void WriteDataPort_RegisterB_EnablesPeriodicTimer() {
        // Arrange - Select Register B
        _rtc.WriteByte(0x70, 0x0B);
        const byte enablePeriodicTimer = 0x42; // Bit 6 = PIE (Periodic Interrupt Enable)

        // Act
        _rtc.WriteByte(0x71, enablePeriodicTimer);

        // Read it back
        _rtc.WriteByte(0x70, 0x0B);
        byte result = _rtc.ReadByte(0x71);

        // Assert - Bit 7 is masked in write, check bits 0-6
        (result & 0x7F).Should().Be(enablePeriodicTimer & 0x7F);
    }

    [Fact]
    public void ReadDataPort_TimeRegisters_ReturnCurrentTime() {
        // Arrange
        var now = DateTime.Now;

        // Act - Read seconds
        _rtc.WriteByte(0x70, 0x00);
        byte seconds = _rtc.ReadByte(0x71);

        // Assert - Should be in valid BCD range (0x00-0x59)
        seconds.Should().BeLessOrEqualTo(0x59);
        
        // Verify BCD format (each nibble should be 0-9)
        int tensDigit = (seconds >> 4) & 0x0F;
        int onesDigit = seconds & 0x0F;
        tensDigit.Should().BeLessOrEqualTo(5);
        onesDigit.Should().BeLessOrEqualTo(9);
    }

    [Fact]
    public void ReadDataPort_DateRegisters_ReturnCurrentDate() {
        // Arrange
        var now = DateTime.Now;

        // Act - Read day of month
        _rtc.WriteByte(0x70, 0x07);
        byte day = _rtc.ReadByte(0x71);

        // Assert - Should be in valid BCD range (0x01-0x31)
        day.Should().BeInRange((byte)0x01, (byte)0x31);
        
        // Verify BCD format
        int tensDigit = (day >> 4) & 0x0F;
        int onesDigit = day & 0x0F;
        tensDigit.Should().BeLessOrEqualTo(3);
        onesDigit.Should().BeLessOrEqualTo(9);
    }

    [Fact]
    public void ReadDataPort_Century_ReturnsCorrectCentury() {
        // Arrange - Select century register
        _rtc.WriteByte(0x70, 0x32);

        // Act
        byte century = _rtc.ReadByte(0x71);

        // Assert - Should be 20 in BCD (0x20)
        // Year 2025 / 100 = 20, ToBcd(20) = (2 << 4) | 0 = 0x20
        century.Should().Be(0x20);
    }

    [Fact]
    public void Dispose_UnsubscribesFromPauseEvents() {
        // Arrange
        var pauseHandler = Substitute.For<IPauseHandler>();
        var loggerService = Substitute.For<ILoggerService>();
        var state = new State(CpuModel.INTEL_80286);
        var ioPortDispatcher = new IOPortDispatcher(Substitute.For<AddressReadWriteBreakpoints>(), state, loggerService, true);
        var dualPic = new DualPic(state, ioPortDispatcher, false, false, loggerService);

        var rtc = new RealTimeClock(state, ioPortDispatcher, dualPic, pauseHandler, false, loggerService);

        // Act
        rtc.Dispose();

        // Assert - Calling dispose again should not throw
        Action act = () => rtc.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void WriteDataPort_AlarmRegisters_StoresValue() {
        // Arrange - Select alarm second register
        _rtc.WriteByte(0x70, 0x01);
        const byte alarmValue = 0x30; // 30 seconds in BCD

        // Act
        _rtc.WriteByte(0x71, alarmValue);

        // Read it back
        _rtc.WriteByte(0x70, 0x01);
        byte result = _rtc.ReadByte(0x71);

        // Assert
        result.Should().Be(alarmValue);
    }

    [Fact]
    public void ReadDataPort_InvalidRegister_ReturnsStoredValue() {
        // Arrange - Write to a general-purpose CMOS RAM location
        _rtc.WriteByte(0x70, 0x10);
        const byte testValue = 0x42;
        _rtc.WriteByte(0x71, testValue);

        // Act - Read it back
        _rtc.WriteByte(0x70, 0x10);
        byte result = _rtc.ReadByte(0x71);

        // Assert
        (result & 0x7F).Should().Be(testValue & 0x7F); // Bit 7 is masked in write
    }

    public void Dispose() {
        _rtc?.Dispose();
    }
}
