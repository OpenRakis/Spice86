namespace Spice86.Converters;

using Avalonia.Data.Converters;

using System.Globalization;

public class HexadecimalToDecimalConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is int) {
            return $"0x{(int)value:X}";
        }
        if (value is long) {
            return $"0x{(long)value:X}";
        }
        if (value is uint) {
            return $"0x{(uint)value:X}";
        }

        return value;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is string stringValue && stringValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            if (int.TryParse(stringValue[2..], NumberStyles.HexNumber, culture, out int intValue)) {
                return intValue;
            }
        }
        return 0;
    }
}
