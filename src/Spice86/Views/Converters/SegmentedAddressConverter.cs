namespace Spice86.Views.Converters;

using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Emulator.Memory;
using Spice86.ViewModels;

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

public class SegmentedAddressConverter : AvaloniaObject, IValueConverter {
    public static readonly StyledProperty<State?> StateProperty = AvaloniaProperty.Register<SegmentedAddressConverter, State?>(nameof(State));

    public State? State {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value switch {
            null => null,
            SegmentedAddress segmentedAddress => segmentedAddress.ToString(),
            _ => new BindingNotification(new InvalidCastException(value.ToString()), BindingErrorType.Error)
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not string inputString || string.IsNullOrWhiteSpace(inputString)) {
            return new SegmentedAddress();
        }

        SegmentedAddress? result = AddressAndValueParser.ParseSegmentedAddress(inputString, State);

        if (result != null) {
            return result;
        }

        return new BindingNotification(new InvalidCastException(value.ToString()), BindingErrorType.Error);
    }

    private static ushort? ParseSegmentOrRegister(string value, State? state) {
        if (ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort result)) {
            return result;
        }

        if (state != null) {
            string propertyName = value.ToUpperInvariant();
            PropertyInfo? property = state.GetType().GetProperty(propertyName);
            if (property != null && property.PropertyType == typeof(ushort) && property.GetValue(state) is ushort propertyValue) {
                return propertyValue;
            }
        }

        return null;
    }
}