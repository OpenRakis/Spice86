using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Globalization;
using System.Text.RegularExpressions;

namespace Spice86.Converters;

public partial class AddressStringToSegmentedAddressConverter : SegmentedAddressConverter
{

    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return base.Convert(value, targetType, parameter, culture);
    }

    public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str || string.IsNullOrWhiteSpace(str))
        {
            return new SegmentedAddress();
        }

        // Match 0x hexadecimal address
        Match hexMatch = HexAddressRegex().Match(str);
        if (hexMatch.Success)
        {
            if (uint.TryParse(hexMatch.Groups[1].Value,
                NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out uint address))
            {
                return MemoryUtils.ToSegmentedAddress(address);
            }
        }

        // Match hexadecimal address without 0x prefix
        Match hexNoPrefixMatch = HexNoPrefixAddressRegex().Match(str);
        if (hexNoPrefixMatch.Success)
        {
            if (uint.TryParse(hexNoPrefixMatch.Groups[1].Value,
                NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out uint address))
            {
                return MemoryUtils.ToSegmentedAddress(address);
            }
        }

        // Match decimal address
        Match decimalMatch = DecimalAddressRegex().Match(str);
        if (decimalMatch.Success)
        {
            if (uint.TryParse(decimalMatch.Groups[1].Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture,
                out uint address))
            {
                return MemoryUtils.ToSegmentedAddress(address);
            }
        }

        // Fallback to base class method
        return base.ConvertBack(value, targetType, parameter, culture);
    }

    [GeneratedRegex(@"^0x([0-9A-Fa-f]+)$")]
    private static partial Regex HexAddressRegex();

    [GeneratedRegex(@"^([0-9A-Fa-f]+)$")]
    private static partial Regex HexNoPrefixAddressRegex();

    [GeneratedRegex(@"^(\d+)$")]
    private static partial Regex DecimalAddressRegex();
}
