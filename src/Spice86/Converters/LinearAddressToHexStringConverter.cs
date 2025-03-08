using Avalonia.Data;
using Avalonia.Data.Converters;

using Spice86.Models.Debugging;

using System.Globalization;

namespace Spice86.Converters;

public class LinearAddressToHexStringConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if(value is LinearMemoryAddress physicalAddress) {
            return physicalAddress.ToString();
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}
