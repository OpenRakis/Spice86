namespace Spice86.Converters;

using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Emulator.Memory;

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

public partial class SegmentedAddressConverter : AvaloniaObject, IValueConverter
{
    public static readonly StyledProperty<State?> StateProperty =
        AvaloniaProperty.Register<SegmentedAddressConverter, State?>(
        nameof(State),
        null,
        false);

    public State? State {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public virtual object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            null => null,
            SegmentedAddress segmentedAddress => $"{segmentedAddress.Segment:X4}:{segmentedAddress.Offset:X4}",
            _ => new BindingNotification(new InvalidCastException(), BindingErrorType.Error)
        };
    }

    public virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str || string.IsNullOrWhiteSpace(str))
        {
            return new SegmentedAddress();
        }

        Match match = SegmentedAddressRegex().Match(str);
        if (match.Success)
        {
            ushort? segment = ParseSegmentOrRegister(match.Groups[1].Value, State);
            ushort? offset = ParseSegmentOrRegister(match.Groups[2].Value, State);
            if(segment.HasValue && offset.HasValue) {
                return new SegmentedAddress(segment.Value, offset.Value);
            }

        }
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    internal static ushort? ParseSegmentOrRegister(string value, State? parameter)
    {
        if (ushort.TryParse(value, NumberStyles.HexNumber, 
            CultureInfo.InvariantCulture, out ushort result))
        {
            return result;
        }

        if (parameter is State state)
        {
            PropertyInfo? property = state.GetType().GetProperty(value.ToUpperInvariant());
            if (property != null &&
                property.PropertyType == typeof(ushort) &&
                property.GetValue(state) is ushort propertyValue)
            {
                return propertyValue;
            }
        }

        return null;
    }

    [GeneratedRegex(@"^([0-9A-Fa-f]{4}|[a-zA-Z]{2}):([0-9A-Fa-f]{4}|[a-zA-Z]{2})$")]
    internal static partial Regex SegmentedAddressRegex();
}
