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
        AvaloniaProperty.Register<AddressStringToLinearMemoryAddresssConverter, State?>(
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
            LinearMemoryAddress linearAddress => linearAddress,
            uint uintAddress => new LinearMemoryAddress(uintAddress),
            _ => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is LinearMemoryAddress linearAddress) {
            return linearAddress;
        }
        if (value is not string str || string.IsNullOrWhiteSpace(str)) {
            return null;
        }

        Match match = SegmentedAddressConverter.SegmentedAddressRegex().Match(str);
        if (match.Success) {
            string inputSegment = match.Groups[1].Value;
            ushort? segment = SegmentedAddressConverter.ParseSegmentOrRegister(
                inputSegment, State);
            string inputOffset = match.Groups[2].Value;
            ushort? offset = SegmentedAddressConverter.ParseSegmentOrRegister(
                inputOffset, State);
            if (segment.HasValue && offset.HasValue) {
                return new LinearMemoryAddress(MemoryUtils.ToPhysicalAddress(
                    segment.Value, offset.Value),
                    $"{inputSegment}:{inputOffset}");
            }
        }

        match = HexAddressRegex().Match(str);

        if (match.Success) {
            string sourceInput = match.Groups[1].Value;
            if (uint.TryParse(sourceInput, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out uint result)) {
                return new LinearMemoryAddress(result, sourceInput);
            }
        }

        match = DecimalAddressRegex().Match(str);

        if (match.Success) {
            string sourceInput = match.Groups[1].Value;
            if(uint.TryParse(sourceInput, out uint result)) {
                return new LinearMemoryAddress(result, sourceInput);
            }
        }
        return BindingOperations.DoNothing;
    }

    [GeneratedRegex(@"^0x([0-9A-Fa-f]+)$")]
    private static partial Regex HexAddressRegex();

    [GeneratedRegex(@"^(\d+)$")]
    private static partial Regex DecimalAddressRegex();
}
