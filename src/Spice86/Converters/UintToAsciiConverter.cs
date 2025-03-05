using Avalonia.Data.Converters;

using System.Globalization;
using System.Text;

namespace Spice86.Converters;
public class UIntToAsciiConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        try {
            if (value is uint uintValue) {
            return Encoding.ASCII.GetString(BitConverter.GetBytes(uintValue));
        }
        } catch (IndexOutOfRangeException) {
            //A read during emulation provoked an OutOfRangeException (in emulated memory).
            // Ignore it.
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
