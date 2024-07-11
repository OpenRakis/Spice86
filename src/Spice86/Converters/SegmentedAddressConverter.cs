namespace Spice86.Converters;

using Avalonia.Data;
using Avalonia.Data.Converters;

using Spice86.Shared.Emulator.Memory;

using System.Globalization;
using System.Text.RegularExpressions;

public partial class SegmentedAddressConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value switch {
            null => null,
            SegmentedAddress segmentedAddress => $"{segmentedAddress.Segment:X4}:{segmentedAddress.Offset:X4}",
            _ => new BindingNotification(new InvalidCastException(), BindingErrorType.Error)
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not string str || string.IsNullOrWhiteSpace(str)) {
            return new SegmentedAddress();
        }

        Match match = SegmentedAddressRegex().Match(str);
        if (match.Success) {
            return new SegmentedAddress(
                ushort.Parse(match.Groups[1].Value, NumberStyles.HexNumber, culture),
                ushort.Parse(match.Groups[2].Value, NumberStyles.HexNumber, culture)
            );
        }

        return null;
    }

    [GeneratedRegex(@"^([0-9A-Fa-f]{4}):([0-9A-Fa-f]{4})$")]
    private static partial Regex SegmentedAddressRegex();
}