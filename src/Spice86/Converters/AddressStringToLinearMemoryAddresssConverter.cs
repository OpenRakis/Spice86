using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

using Spice86.Core.Emulator.CPU;
using Spice86.Models.Debugging;
using Spice86.Shared.Utils;

using System.Globalization;
using System.Text.RegularExpressions;

namespace Spice86.Converters;

public partial class AddressStringToLinearMemoryAddresssConverter : AvaloniaObject, IValueConverter {
    public static StyledProperty<State?> StateProperty =
        AvaloniaProperty.Register<SegmentedAddressConverter, State?>(
        nameof(State),
        null,
        false);

    public State? State {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value switch {
            null => null,
            LinearMemoryAddress linearAddress => linearAddress.ToString(),
            uint uintAddress => $"0x{uintAddress:X}",
            _ => BindingNotification.Null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not string str || string.IsNullOrWhiteSpace(str)) {
            return BindingNotification.Null;
        }

        Match match = SegmentedAddressConverter.SegmentedAddressRegex().Match(str);
        if (match.Success) {
            ushort? segment = SegmentedAddressConverter.ParseSegmentOrRegister(match.Groups[1].Value, State);
            ushort? offset = SegmentedAddressConverter.ParseSegmentOrRegister(match.Groups[2].Value, State);
            if (segment.HasValue && offset.HasValue) {
                return new LinearMemoryAddress(MemoryUtils.ToPhysicalAddress(segment.Value, offset.Value));
            }
        }

        match = HexAddressRegex().Match(str);

        if (match.Success) {
            return new LinearMemoryAddress(uint.Parse(match.Groups[1].Value, NumberStyles.HexNumber));
        }

        match = DecimalAddressRegex().Match(str);

        if (match.Success) {
            return new LinearMemoryAddress(uint.Parse(match.Groups[1].Value));
        }
        return BindingNotification.Null;
    }

    [GeneratedRegex(@"^0x([0-9A-Fa-f]+)$")]
    private static partial Regex HexAddressRegex();

    [GeneratedRegex(@"^(\d+)$")]
    private static partial Regex DecimalAddressRegex();
}
