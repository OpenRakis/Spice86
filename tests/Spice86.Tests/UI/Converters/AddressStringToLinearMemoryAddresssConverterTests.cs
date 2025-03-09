using Avalonia.Data;

using FluentAssertions;

using Spice86.Converters;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Models.Debugging;

using System.Globalization;

using Xunit;

namespace Spice86.Tests.UI.Converters;

public class AddressStringToLinearMemoryAddresssConverterTests {
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
    public void Convert_LinearMemoryAddress_ReturnsLinearMemoryAddressUnchanged() {
        // Arrange
        var address = new LinearMemoryAddress(0x1234);

        // Act
        object? result = _converter.Convert(address, typeof(string),
            null, CultureInfo.InvariantCulture);

        // Assert
        result?.Should().BeEquivalentTo(new LinearMemoryAddress(4660));
    }

    [Fact]
    public void ConvertBack_DecimalAddress_ReturnsLinearMemoryAddress() {
        // Arrange
        string address = A20Gate.EndOfHighMemoryArea.ToString(CultureInfo.InvariantCulture);

        // Act
        object? result = _converter.ConvertBack(address, typeof(LinearMemoryAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        new LinearMemoryAddress(A20Gate.EndOfHighMemoryArea).Should().BeEquivalentTo(result);
    }

    [Fact]
    public void ConvertBack_HexadecimallAddress_ReturnsLinearMemoryAddress() {
        // Arrange
        string address = "0xFFFF";

        // Act
        object? result = _converter.ConvertBack(address, typeof(LinearMemoryAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        new LinearMemoryAddress(65535).Should().BeEquivalentTo(result);
    }

    [Fact]
    public void Convert_InvalidType_ReturnsNull() {
        // Arrange
        object value = 12345;

        // Act
        object? result = _converter.Convert(value, typeof(string),
            null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertBack_NullOrWhitespaceString_ReturnsNull() {
        // Arrange
        object? value = null;

        // Act
        object? result = _converter.ConvertBack(value, typeof(LinearMemoryAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Null(result);

        // Arrange
        value = "   ";

        // Act
        result = _converter.ConvertBack(value, typeof(LinearMemoryAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertBack_ValidString_ReturnsLinearMemoryAddress() {
        // Arrange
        object value = "1234:5678";

        // Act
        object? result = _converter.ConvertBack(value, typeof(LinearMemoryAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        new LinearMemoryAddress(96696).Should().BeEquivalentTo(result);
    }

    [Fact]
    public void ConvertBack_InvalidString_ReturnsBindingOperationDoNothing() {
        // Arrange
        object value = "invalid";

        // Act
        object? result = _converter.ConvertBack(value, typeof(LinearMemoryAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        BindingOperations.DoNothing.Should().BeEquivalentTo(result);
    }

    [Fact]
    public void ConvertBack_ValidStringWithState_ReturnsLinearMemoryAddress() {
        // Arrange
        var state = new State { AX = 0x1234, BX = 0x5678 };
        _converter.State = state;
        object value = "AX:BX";

        // Act
        object? result = _converter.ConvertBack(value, typeof(LinearMemoryAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        new LinearMemoryAddress(96696).Should().BeEquivalentTo(result);
    }

    [Fact]
    public void ConvertBack_InvalidStringWithState_ReturnsBindingOperationDoNothing() {
        // Arrange
        var state = new State { AX = 0x1234, BX = 0x5678 };
        _converter.State = state;
        object value = "invalid:BX";

        // Act
        object? result = _converter.ConvertBack(value, typeof(LinearMemoryAddress),
            null, CultureInfo.InvariantCulture);

        // Assert
        BindingOperations.DoNothing.Should().BeEquivalentTo(result);
    }
}
