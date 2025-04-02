namespace Spice86.ViewModels;

using Spice86.Converters;
using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Utils;

using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

/// <summary>
/// Validates a memory address string to ensure it is a valid address. <br/>
/// Input format can be 0x (HEX) or FFFF:FFFF (SEG:OFF) <br/>
/// </summary>
public abstract partial class AddressValidatorBaseViewModel : ViewModelBase,
    INotifyDataErrorInfo {
    protected readonly State _state;
    protected readonly Dictionary<string, List<string>> _errors = new();

    public AddressValidatorBaseViewModel(State state) {
        _state = state;
    }

    private static bool TryParseSegmentOrRegister(string value, State? parameter,
        [NotNullWhen(true)] out ushort? @ushort) {
        if (ushort.TryParse(value, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out ushort result)) {
            @ushort = result;
            return true;
        }

        if (parameter is State state) {
            PropertyInfo? property = state.GetType().GetProperty(value.ToUpperInvariant());
            if (property != null &&
                property.PropertyType == typeof(ushort) &&
                property.GetValue(state) is ushort propertyValue) {
                @ushort = propertyValue;
                return true;
            }
        }

        @ushort = null;
        return false;
    }

    [GeneratedRegex(@"^([0-9A-Fa-f]{4}|[a-zA-Z]{2}):([0-9A-Fa-f]{4}|[a-zA-Z]{2})$")]
    private static partial Regex SegmentedAddressRegex();

    [GeneratedRegex(@"^0x[0-9A-Fa-f]{1,8}$")]
    protected static partial Regex HexAddressRegex();

    protected bool TryValidateAddress(string? value, out string message) {
        if (string.IsNullOrWhiteSpace(value)) {
            message = "Address is required";
            return false;
        }
        if (!HexAddressRegex().IsMatch(value) &&
            !SegmentedAddressRegex().IsMatch(value)) {
            message = "Invalid address format";
            return false;
        }

        if (!TryParseAddressString(value, out uint? _)) {
            message = "Invalid address";
            return false;
        }

        message = string.Empty;
        return true;
    }

    protected bool ValidateAddressProperty(object? value, [CallerMemberName]
        string? propertyName = null) {
        if (string.IsNullOrWhiteSpace(propertyName)) {
            return true;
        }

        bool status = TryValidateAddress(value as string, out string? error);
        if (!status) {
            if (!_errors.TryGetValue(propertyName,
                out List<string>? values)) {
                values = new List<string>();
                _errors[propertyName] = values;
            }
            values.Clear();
            values.Add(error);
        } else {
            _errors.Remove(propertyName);
        }

        OnErrorsChanged(propertyName);
        return status;
    }

    public bool HasErrors => _errors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName) {
        if (propertyName is not null &&
            _errors.TryGetValue(propertyName, out List<string>? value)) {
            return value;
        }
        return Array.Empty<string>();
    }

    protected void OnErrorsChanged(string propertyName) {
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Tries to parse the address string into a uint address.
    /// </summary>
    /// <param name="value">The user input.</param>
    /// <param name="address">The parsed address. <c>null</c> if we return <c>false</c></param>
    /// <returns>A boolean value indicating success or error, along with the address out variable.</returns>
    protected bool TryParseAddressString(string? value, [NotNullWhen(true)] out uint? address) {
        if (string.IsNullOrWhiteSpace(value)) {
            address = null;
            return false;
        }
        Match segmentedMatch = SegmentedAddressRegex()
            .Match(value);
        if (HexAddressRegex().Match(value) is Match hexMatch) {
            string hexValue = hexMatch.Value;
            if (hexValue.StartsWith("0x") && hexValue.Length > 2) {
                hexValue = hexValue[2..];
            }
            if (uint.TryParse(hexValue, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out uint hexAddress)) {
                address = hexAddress;
                return true;
            }
        }
        if (segmentedMatch.Success &&
            TryParseSegmentOrRegister(
            segmentedMatch.Groups[1].Value, _state,
            out ushort? segment) &&
            TryParseSegmentOrRegister(
            segmentedMatch.Groups[2].Value, _state,
            out ushort? offset)) {
            address = MemoryUtils.ToPhysicalAddress(segment.Value,
                offset.Value);
            return true;
        }
        address = null;
        return false;
    }
}
