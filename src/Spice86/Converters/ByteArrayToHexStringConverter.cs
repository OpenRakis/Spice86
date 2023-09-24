namespace Spice86.Converters;

using Avalonia.Data.Converters;

using System.Globalization;

public class ByteArrayToHexStringConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is byte[] bytes) {
            return System.Convert.ToHexString(bytes);
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return Array.Empty<byte>();
    }
}