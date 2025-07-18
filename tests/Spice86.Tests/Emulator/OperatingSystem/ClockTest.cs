namespace Spice86.Tests.Emulator.OperatingSystem;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Interfaces;

using Xunit;

public class ClockTests {
    private readonly ILoggerService _loggerService;
    private readonly Clock _clock;

    public ClockTests() {
        _loggerService = Substitute.For<ILoggerService>();
        _clock = new Clock(_loggerService);
    }

    [Fact]
    public void Constructor_InitializesWithNoOffset() {
        // Arrange & Act
        var clock = new Clock(_loggerService);

        // Assert
        clock.HasOffset.Should().BeFalse();
        clock.GetVirtualDateTime().Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(12, 30, 45, 50)]
    [InlineData(23, 59, 59, 99)]
    public void SetTime_ValidTime_ReturnsTrue(byte hours, byte minutes, byte seconds, byte hundredths) {
        // Act
        bool result = _clock.SetTime(hours, minutes, seconds, hundredths);

        // Assert
        result.Should().BeTrue();
        _clock.HasOffset.Should().BeTrue();
    }

    [Theory]
    [InlineData(24, 0, 0, 0)]
    [InlineData(0, 60, 0, 0)]
    [InlineData(0, 0, 60, 0)]
    [InlineData(0, 0, 0, 100)]
    public void SetTime_InvalidTime_ReturnsFalse(byte hours, byte minutes, byte seconds, byte hundredths) {
        // Act
        bool result = _clock.SetTime(hours, minutes, seconds, hundredths);

        // Assert
        result.Should().BeFalse();
        _clock.HasOffset.Should().BeFalse();
    }

    [Theory]
    [InlineData(2023, 1, 1)]
    [InlineData(2024, 12, 31)]
    [InlineData(1980, 6, 15)]
    public void SetDate_ValidDate_ReturnsTrue(ushort year, byte month, byte day) {
        // Act
        bool result = _clock.SetDate(year, month, day);

        // Assert
        result.Should().BeTrue();
        _clock.HasOffset.Should().BeTrue();
    }

    [Theory]
    [InlineData(2023, 0, 1)]
    [InlineData(2023, 13, 1)]
    [InlineData(2023, 1, 0)]
    [InlineData(2023, 1, 32)]
    [InlineData(2023, 2, 29)] // Not a leap year
    public void SetDate_InvalidDate_ReturnsFalse(ushort year, byte month, byte day) {
        // Act
        bool result = _clock.SetDate(year, month, day);

        // Assert
        result.Should().BeFalse();
        _clock.HasOffset.Should().BeFalse();
    }

    [Fact]
    public void GetTime_WithTimeOffset_ReturnsVirtualTime() {
        // Arrange
        byte expectedHours = 15;
        byte expectedMinutes = 30;
        byte expectedSeconds = 45;
        byte expectedHundredths = 50;

        // Act
        _clock.SetTime(expectedHours, expectedMinutes, expectedSeconds, expectedHundredths);
        var (hours, minutes, seconds, hundredths) = _clock.GetTime();

        // Assert
        hours.Should().Be(expectedHours);
        minutes.Should().Be(expectedMinutes);
        seconds.Should().Be(expectedSeconds);
        hundredths.Should().Be(expectedHundredths);
    }

    [Fact]
    public void GetDate_WithDateOffset_ReturnsVirtualDate() {
        // Arrange
        ushort expectedYear = 2023;
        byte expectedMonth = 6;
        byte expectedDay = 15;

        // Act
        _clock.SetDate(expectedYear, expectedMonth, expectedDay);
        var (year, month, day, dayOfWeek) = _clock.GetDate();

        // Assert
        year.Should().Be(expectedYear);
        month.Should().Be(expectedMonth);
        day.Should().Be(expectedDay);
        dayOfWeek.Should().Be((byte)new DateTime(expectedYear, expectedMonth, expectedDay).DayOfWeek);
    }

    [Fact]
    public void GetTime_WithoutOffset_ReturnsRealTime() {
        // Arrange
        DateTime now = DateTime.Now;

        // Act
        var (hours, minutes, seconds, hundredths) = _clock.GetTime();

        // Assert
        hours.Should().Be((byte)now.Hour);
        minutes.Should().Be((byte)now.Minute);
        seconds.Should().BeCloseTo((byte)now.Second, 1);
        hundredths.Should().BeCloseTo((byte)(now.Millisecond / 10), 10);
    }

    [Fact]
    public void GetDate_WithoutOffset_ReturnsRealDate() {
        // Arrange
        DateTime now = DateTime.Now;

        // Act
        var (year, month, day, dayOfWeek) = _clock.GetDate();

        // Assert
        year.Should().Be((ushort)now.Year);
        month.Should().Be((byte)now.Month);
        day.Should().Be((byte)now.Day);
        dayOfWeek.Should().Be((byte)now.DayOfWeek);
    }

    [Fact]
    public void GetVirtualDateTime_WithBothOffsets_AppliesBoth() {
        // Arrange
        ushort virtualYear = 2024;
        byte virtualMonth = 12;
        byte virtualDay = 25;
        byte virtualHours = 18;
        byte virtualMinutes = 45;
        byte virtualSeconds = 30;
        byte virtualHundredths = 0;

        // Act
        _clock.SetDate(virtualYear, virtualMonth, virtualDay);
        _clock.SetTime(virtualHours, virtualMinutes, virtualSeconds, virtualHundredths);
        DateTime virtualDateTime = _clock.GetVirtualDateTime();

        // Assert
        virtualDateTime.Year.Should().Be(virtualYear);
        virtualDateTime.Month.Should().Be(virtualMonth);
        virtualDateTime.Day.Should().Be(virtualDay);
        virtualDateTime.Hour.Should().Be(virtualHours);
        virtualDateTime.Minute.Should().Be(virtualMinutes);
        virtualDateTime.Second.Should().Be(virtualSeconds);
    }

    [Fact]
    public void GetVirtualDateTime_WithOnlyDateOffset_AppliesDateOnly() {
        // Arrange
        DateTime now = DateTime.Now;
        ushort virtualYear = 2024;
        byte virtualMonth = 12;
        byte virtualDay = 25;

        // Act
        _clock.SetDate(virtualYear, virtualMonth, virtualDay);
        DateTime virtualDateTime = _clock.GetVirtualDateTime();

        // Assert
        virtualDateTime.Year.Should().Be(virtualYear);
        virtualDateTime.Month.Should().Be(virtualMonth);
        virtualDateTime.Day.Should().Be(virtualDay);
        virtualDateTime.Hour.Should().Be(now.Hour);
        virtualDateTime.Minute.Should().Be(now.Minute);
        virtualDateTime.Second.Should().BeCloseTo(now.Second, 1);
    }

    [Fact]
    public void GetVirtualDateTime_WithOnlyTimeOffset_AppliesTimeOnly() {
        // Arrange
        DateTime now = DateTime.Now;
        byte virtualHours = 18;
        byte virtualMinutes = 45;
        byte virtualSeconds = 30;
        byte virtualHundredths = 0;

        // Act
        _clock.SetTime(virtualHours, virtualMinutes, virtualSeconds, virtualHundredths);
        DateTime virtualDateTime = _clock.GetVirtualDateTime();

        // Assert
        virtualDateTime.Year.Should().Be(now.Year);
        virtualDateTime.Month.Should().Be(now.Month);
        virtualDateTime.Day.Should().Be(now.Day);
        virtualDateTime.Hour.Should().Be(virtualHours);
        virtualDateTime.Minute.Should().Be(virtualMinutes);
        virtualDateTime.Second.Should().Be(virtualSeconds);
    }

    [Fact]
    public void GetVirtualDateTime_WithoutOffset_ReturnsRealTime() {
        // Arrange
        DateTime now = DateTime.Now;

        // Act
        DateTime virtualDateTime = _clock.GetVirtualDateTime();

        // Assert
        virtualDateTime.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void HasOffset_ReturnsTrue_WhenTimeOffsetSet() {
        // Act
        _clock.SetTime(12, 0, 0, 0);

        // Assert
        _clock.HasOffset.Should().BeTrue();
    }

    [Fact]
    public void HasOffset_ReturnsTrue_WhenDateOffsetSet() {
        // Act
        _clock.SetDate(2023, 6, 15);

        // Assert
        _clock.HasOffset.Should().BeTrue();
    }

    [Fact]
    public void HasOffset_ReturnsTrue_WhenBothOffsetsSet() {
        // Act
        _clock.SetDate(2023, 6, 15);
        _clock.SetTime(12, 0, 0, 0);

        // Assert
        _clock.HasOffset.Should().BeTrue();
    }

    [Fact]
    public void SetTime_OverridesPreviousTimeOffset() {
        // Arrange
        _clock.SetTime(12, 0, 0, 0);

        // Act
        _clock.SetTime(18, 30, 45, 50);
        var (hours, minutes, seconds, hundredths) = _clock.GetTime();

        // Assert
        hours.Should().Be(18);
        minutes.Should().Be(30);
        seconds.Should().Be(45);
        hundredths.Should().Be(50);
    }

    [Fact]
    public void SetDate_OverridesPreviousDateOffset() {
        // Arrange
        _clock.SetDate(2023, 1, 1);

        // Act
        _clock.SetDate(2024, 12, 31);
        var (year, month, day, _) = _clock.GetDate();

        // Assert
        year.Should().Be(2024);
        month.Should().Be(12);
        day.Should().Be(31);
    }
}