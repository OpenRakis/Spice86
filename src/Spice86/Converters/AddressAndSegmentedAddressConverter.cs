using Avalonia.Data;
using Avalonia.Data.Converters;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Globalization;

using Tmds.DBus.Protocol;

namespace Spice86.Converters;

public class AddressAndSegmentedAddressConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is SegmentedAddress segmentedAddress) {
            uint address = segmentedAddress.ToPhysical();
            return $"0x{address:X4} ({segmentedAddress})";
        }
        if(value is uint physicalAddress) {
            SegmentedAddress segmentedAddressFromPhysical = MemoryUtils.ToSegmentedAddress(physicalAddress);
            return $"0x{segmentedAddressFromPhysical.ToPhysical():X4} ({segmentedAddressFromPhysical})";
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}
