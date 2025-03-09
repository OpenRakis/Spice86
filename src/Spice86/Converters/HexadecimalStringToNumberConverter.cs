using Avalonia.Data.Converters;

using System.Globalization;

namespace Spice86.Converters;

public class HexadecimalStringToNumberConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is ushort us) {
            return us.ToString("X");
        }
        if(value is uint ui) {
            return ui.ToString("X");
        }
        if (value is int i) {
            return i.ToString("X");
        }
        if (value is long l) {
            return l.ToString("X");
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is string str) {
            if (str.StartsWith("0x",
                StringComparison.InvariantCultureIgnoreCase) &&
                str.Length > 2) {
                str = str[2..];
            } else if (str.EndsWith("H",
                StringComparison.InvariantCultureIgnoreCase) &&
                str.Length > 2) {
                str = str[..^1];
            }
            if ((targetType == typeof(ushort?) ||
                targetType == typeof(ushort)) && ushort.TryParse(
                str, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out ushort resultUshort)) {
                return resultUshort;
            }
            if ((targetType == typeof(int?) ||
                targetType == typeof(int)) && int.TryParse(
                str, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out int resultInt)) {
                return resultInt;
            }
            if (long.TryParse(
                str, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out long resultLong)) {
                return resultLong;
            }
        }
        return null;
       
    }
}
