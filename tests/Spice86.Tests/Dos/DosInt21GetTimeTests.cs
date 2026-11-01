namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Tests.Utility;

using Xunit;

public sealed class DosInt21GetTimeTests : IDisposable {
    private readonly TempFile _tempFile;
    private readonly DosTestFixture _fixture;

    public DosInt21GetTimeTests() {
        _tempFile = new TempFile("Spice86_Int21_GetTime_Tests");
        _fixture = new DosTestFixture(_tempFile.Path);
    }

    public void Dispose() {
        _fixture.Dispose();
        _tempFile.Dispose();
    }

    [Fact]
    public void GetTime_ReturnsHundredthsFromEmulatedClock() {
        // Arrange
        _fixture.EmulatedClock.StartTime = new DateTimeOffset(1993, 6, 1, 10, 20, 30, 450, TimeSpan.Zero);
        _fixture.State.AH = 0x2C;

        // Act
        _fixture.DosInt21Handler.RunFromOverriden();

        // Assert
        DateTimeOffset expected = _fixture.EmulatedClock.CurrentDateTime;
        _fixture.State.CH.Should().Be((byte)expected.Hour);
        _fixture.State.CL.Should().Be((byte)expected.Minute);
        _fixture.State.DH.Should().Be((byte)expected.Second);
        _fixture.State.DL.Should().Be((byte)(expected.Millisecond / 10));
    }

    [Fact]
    public void GetTime_RtcInBinaryMode_ReturnsTheClockTime() {
        // Arrange
        _fixture.EmulatedClock.StartTime = new DateTimeOffset(1993, 6, 1, 0, 0, 32, 450, TimeSpan.Zero);
        // Status Register B bit 2 selects the RTC binary data mode.
        _fixture.IoPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterB);
        byte statusB = _fixture.IoPortDispatcher.ReadByte(CmosPorts.Data);
        _fixture.IoPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterB);
        _fixture.IoPortDispatcher.WriteByte(CmosPorts.Data, (byte)(statusB | 0x04));
        _fixture.State.AH = 0x2C;

        // Act
        _fixture.DosInt21Handler.RunFromOverriden();

        // Assert
        DateTimeOffset expected = _fixture.EmulatedClock.CurrentDateTime;
        _fixture.State.CH.Should().Be((byte)expected.Hour);
        _fixture.State.CL.Should().Be((byte)expected.Minute);
        _fixture.State.DH.Should().Be((byte)expected.Second);
        _fixture.State.DL.Should().Be((byte)(expected.Millisecond / 10));
    }
}
