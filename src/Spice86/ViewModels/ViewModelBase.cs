namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Utils;

using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

public abstract partial class ViewModelBase : ObservableObject, INotifyDataErrorInfo {
    protected readonly Dictionary<string, List<string>> _validationErrors = new();
    public bool HasErrors => _validationErrors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName) {
        if (propertyName is not null &&
            _validationErrors.TryGetValue(propertyName, out List<string>? value)) {
            return value;
        }
        return Array.Empty<string>();
    }

    protected void OnErrorsChanged(string propertyName) {
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(
            propertyName));
    }

    protected bool TryValidateRequiredPropertyIsNotNull<T>(
        T? value, [NotNullWhen(true)] out T? validatedValue,
            [CallerMemberName] string? bindedPropertyName = null) {
        if (string.IsNullOrWhiteSpace(bindedPropertyName)) {
            validatedValue = default;
            return false;
        }
        if (value is not null ||
            value is string stringValue && !string.IsNullOrWhiteSpace(
                stringValue)) {
            validatedValue = value;
            return true;
        }
        if (!_validationErrors.TryGetValue(bindedPropertyName, out List<string>? values)) {
            _validationErrors.Add(bindedPropertyName, ["This field is required."]);
        } else {
            values.Clear();
            values.Add("This field is required.");
        }
        OnErrorsChanged(bindedPropertyName);
        validatedValue = default;
        return false;
    }

    protected bool GetIsMemoryRangeValid(uint? startAddress, uint? endAddress) {
        return startAddress <= endAddress
        && endAddress >= startAddress;
    }

    protected bool ValidateAddressRange(State state, string? startAddress,
        string? endAddress, string textBoxBindedPropertyName) {
        const string RangeError = "Invalid address range.";
        const string StartError = "Invalid start address.";
        const string EndError = "Invalid end address.";
        bool rangeStatus = false;
        bool statusStart = TryValidateAddress(startAddress, state, out _);
        bool statusEnd = TryValidateAddress(endAddress, state, out _);
        if (statusStart && statusEnd) {
            if (TryParseAddressString(startAddress, state, out uint? start) &&
                TryParseAddressString(endAddress, state, out uint? end)) {
                rangeStatus = GetIsMemoryRangeValid(start, end);
            }
        }
        if (!rangeStatus || !statusStart || !statusEnd) {
            if (!_validationErrors.TryGetValue(textBoxBindedPropertyName,
            out List<string>? values)) {
                values = new List<string>();
                _validationErrors[nameof(textBoxBindedPropertyName)] = values;
            }
            values.Clear();
            if (!rangeStatus) {
                values.Add(RangeError);
            }
            if (!statusStart) {
                values.Add(StartError);
            }
            if (!statusEnd) {
                values.Add(EndError);
            }
            OnErrorsChanged(textBoxBindedPropertyName);
        }
        return rangeStatus && statusStart && statusEnd;
    }

    private static bool TryParseSegmentOrRegister(string value, State state,
            [NotNullWhen(true)] out ushort? @ushort) {
        PropertyInfo? property = state.GetType().GetProperty(value.ToUpperInvariant());
        if (property != null &&
            property.PropertyType == typeof(ushort) &&
            property.GetValue(state) is ushort propertyValue) {
            @ushort = propertyValue;
            return true;
        } else if (value.Length == 4 &&
            ushort.TryParse(value, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out ushort result)) {
            @ushort = result;
            return true;
        }

        @ushort = null;
        return false;
    }

    [GeneratedRegex(@"^([0-9A-Fa-f]{4}|[a-zA-Z]{2}):([0-9A-Fa-f]{4}|[a-zA-Z]{2})$")]
    private static partial Regex SegmentedAddressRegex();

    [GeneratedRegex(@"^0x[0-9A-Fa-f]{1,8}$")]
    protected static partial Regex HexAddressRegex();

    protected static bool TryValidateAddress(string? value, State state, out string message) {
        if (string.IsNullOrWhiteSpace(value)) {
            message = "Address is required";
            return false;
        }
        if (!HexAddressRegex().IsMatch(value) &&
            !SegmentedAddressRegex().IsMatch(value)) {
            message = "Invalid address format";
            return false;
        }

        if (!TryParseAddressString(value, state, out uint? _)) {
            message = "Invalid address";
            return false;
        }

        message = string.Empty;
        return true;
    }

    protected bool ValidateAddressProperty(object? value, State state, [CallerMemberName]
        string? propertyName = null) {
        if (string.IsNullOrWhiteSpace(propertyName)) {
            return true;
        }

        bool status = TryValidateAddress(value as string, state, out string? error);
        if (!status) {
            if (!_validationErrors.TryGetValue(propertyName,
                out List<string>? values)) {
                values = new List<string>();
                _validationErrors[propertyName] = values;
            }
            values.Clear();
            values.Add(error);
        } else {
            _validationErrors.Remove(propertyName);
        }
        OnErrorsChanged(propertyName);
        return status;
    }

    /// <summary>
    /// Tries to parse the address string into a uint address.
    /// </summary>
    /// <param name="value">The user input.</param>
    /// <param name="address">The parsed address. <c>null</c> if we return <c>false</c></param>
    /// <returns>A boolean value indicating success or error, along with the address out variable.</returns>
    protected static bool TryParseAddressString(string? value, State state, [NotNullWhen(true)] out uint? address) {
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
                segmentedMatch.Groups[1].Value,
                state,
                out ushort? segment)
            &&
            TryParseSegmentOrRegister(
                segmentedMatch.Groups[2].Value,
                state,
                out ushort? offset)) {
            address = MemoryUtils.ToPhysicalAddress(segment.Value,
                offset.Value);
            return true;
        }
        address = null;
        return false;
    }
}
