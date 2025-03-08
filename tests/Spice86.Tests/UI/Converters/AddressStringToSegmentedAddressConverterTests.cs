using Avalonia.Data;

using FluentAssertions;

using Spice86.Converters;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

using System.Globalization;

using Xunit;

namespace Spice86.Tests.UI.Converters;

public class AddressStringToSegmentedAddressConverterTests {
    private readonly AddressStringToLinearMemoryAddresssConverter _converter = new();

    [Fact]
    public void Convert_NullValue_ReturnsNull() {
        // Arrange
        object? value = null;

        // Act
        object? result = _converter.Convert(value, typeof(string),
            null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Convert_SegmentedAddress_ReturnsFormattedString() {
        // Arrange
        var address = new SegmentedAddress(0x1234, 0x5678);

        // Act
        object? result = _converter.Convert(address, typeof(string),
            null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("1234:5678", result);
    }

    [Fact]
    public void ConvertBack_DecimalAddress_ReturnsSegmentedAddress() {
        // Arrange
        string address = A20Gate.EndOfHighMemoryArea.ToString(CultureInfo.InvariantCulture);

        // Act
        object? result = _converter.ConvertBack(address, typeof(SegmentedAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        new SegmentedAddress(0xFFFF, 0xFFFF).Should().BeEquivalentTo(result);
    }

    [Fact]
    public void ConvertBack_HexadecimallAddress_ReturnsSegmentedAddress() {
        // Arrange
        string address = "0xFFFF";

        // Act
        object? result = _converter.ConvertBack(address, typeof(SegmentedAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        new SegmentedAddress(0xFFF, 0x000F).Should().BeEquivalentTo(result);
    }

    [Fact]
    public void ConvertBack_HexadecimallAddress_WithNo0x_ReturnsSegmentedAddress() {
        // Arrange
        string address = "FFFF";

        // Act
        object? result = _converter.ConvertBack(address, typeof(SegmentedAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        new SegmentedAddress(0xFFF, 0x000F).Should().BeEquivalentTo(result);
    }


    [Fact]
    public void Convert_InvalidType_ReturnsBindingNotification() {
        // Arrange
        object value = 12345;

        // Act
        object? result = _converter.Convert(value, typeof(string),
            null, CultureInfo.InvariantCulture);

        // Assert
        Assert.IsType<BindingNotification>(result);
    }

    [Fact]
    public void ConvertBack_NullOrWhitespaceString_ReturnsDefaultSegmentedAddress() {
        // Arrange
        object? value = null;

        // Act
        object? result = _converter.ConvertBack(value, typeof(SegmentedAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        new SegmentedAddress().Should().BeEquivalentTo(result);

        // Arrange
        value = "   ";

        // Act
        result = _converter.ConvertBack(value, typeof(SegmentedAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        new SegmentedAddress().Should().BeEquivalentTo(result);
    }

    [Fact]
    public void ConvertBack_ValidString_ReturnsSegmentedAddress() {
        // Arrange
        object value = "1234:5678";

        // Act
        object? result = _converter.ConvertBack(value, typeof(SegmentedAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        new SegmentedAddress(0x1234, 0x5678).Should().BeEquivalentTo(result);
    }

    [Fact]
    public void ConvertBack_InvalidString_ReturnsBindingNotification() {
        // Arrange
        object value = "invalid";

        // Act
        object? result = _converter.ConvertBack(value, typeof(SegmentedAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        Assert.IsType<BindingNotification>(result);
    }

    [Fact]
    public void ConvertBack_ValidStringWithState_ReturnsSegmentedAddress() {
        // Arrange
        var state = new State { AX = 0x1234, BX = 0x5678 };
        _converter.State = state;
        object value = "AX:BX";

        // Act
        object? result = _converter.ConvertBack(value, typeof(SegmentedAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        new SegmentedAddress(0x1234, 0x5678).Should().BeEquivalentTo(result);
    }

    [Fact]
    public void ConvertBack_InvalidStringWithState_ReturnsBindingNotification() {
        // Arrange
        var state = new State { AX = 0x1234, BX = 0x5678 };
        _converter.State = state;
        object value = "invalid:BX";

        // Act
        object? result = _converter.ConvertBack(value, typeof(SegmentedAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        Assert.IsType<BindingNotification>(result);
    }

    [Fact]
    public void ConvertBack_ValidStringWithLenientMode_ReturnsDefaultSegmentedAddress() {
        // Arrange
        object value = "invalid";
        object parameter = "lenientMode";

        // Act
        object? result = _converter.ConvertBack(value, typeof(SegmentedAddress),
            parameter, CultureInfo.InvariantCulture);

        // Assert
        new SegmentedAddress(0, 0).Should().BeEquivalentTo(result);
    }
}
