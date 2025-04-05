namespace Spice86.Converters;

using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Emulator.Memory;

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

public partial class SegmentedAddressConverter : AvaloniaObject, IValueConverter {
    public static readonly StyledProperty<State?> StateProperty = AvaloniaProperty.Register<SegmentedAddressConverter, State?>(nameof(State));

    public State? State {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        object? result = value switch {
            null => null,
            SegmentedAddress segmentedAddress => segmentedAddress.ToString(),
            _ => new BindingNotification(new InvalidCastException(value.ToString()), BindingErrorType.Error)
        };
        Console.WriteLine("SegmentedAddressConverter.Convert: {0} [{1}]", result, result?.GetType());
        return result;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not string inputString || string.IsNullOrWhiteSpace(inputString)) {
            return new SegmentedAddress();
        }

        Match match = SegmentedAddressRegex().Match(inputString);
        if (match.Success) {
            ushort? segment = ParseSegmentOrRegister(match.Groups[1].Value, State);
            ushort? offset = ParseSegmentOrRegister(match.Groups[2].Value, State);
            if (segment.HasValue && offset.HasValue) {
                var result = new SegmentedAddress(segment.Value, offset.Value);
                Console.WriteLine("SegmentedAddressConverter.ConvertBack: {0} [{1}]", result, result.GetType());
                return result;
            }
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

    [GeneratedRegex(@"^([0-9A-Fa-f]{4}|[a-zA-Z]{2}):([0-9A-Fa-f]{4}|[a-zA-Z]{2})$")]
    private static partial Regex SegmentedAddressRegex();
}