using Avalonia.Data;
using Avalonia.Data.Converters;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Globalization;

namespace Spice86.Converters;

public partial class SegmentedAddressToLinearAddressConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if(value is SegmentedAddress segmentedAddress) {
            return MemoryUtils.ToPhysicalAddress(segmentedAddress.Segment, segmentedAddress.Offset);
        }
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        //This is a one-way converter
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
}
