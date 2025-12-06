namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
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

    protected void ValidateMemoryAddressIsWithinLimit(State state, string? value,
        uint limit = A20Gate.EndOfHighMemoryArea,
        [CallerMemberName] string? bindedPropertyName = null) {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(
            bindedPropertyName);
        if (AddressAndValueParser.TryParseAddressString(value, state, out uint? address) &&
            !GetIsMemoryRangeValid(address, limit, 0)) {
            if (!_validationErrors.TryGetValue(bindedPropertyName,
                out List<string>? values)) {
                values = new List<string>();
                _validationErrors[bindedPropertyName] = values;
            }
            values.Clear();
            values.Add("Value is beyond addressable range");
        }
        OnErrorsChanged(bindedPropertyName);
    }

    protected void ValidateRequiredPropertyIsNotNull<T>(T? value,
        [CallerMemberName] string? bindedPropertyName = null) {
        if (string.IsNullOrWhiteSpace(bindedPropertyName)) {
            return;
        }
        if (value is null ||
            (value is string stringValue && string.IsNullOrWhiteSpace(
                stringValue))) {
            if (!_validationErrors.TryGetValue(bindedPropertyName, out List<string>? values)) {
                _validationErrors.Add(bindedPropertyName, ["This field is required."]);
            } else {
                values.Clear();
                values.Add("This field is required.");
            }
        } else {
            _validationErrors.Remove(bindedPropertyName);
        }
        OnErrorsChanged(bindedPropertyName);
    }

    protected bool GetIsMemoryRangeValid(uint? startAddress, uint? endAddress, uint minRangeWidth) {
        if (startAddress is null || endAddress is null) {
            return false;
        }
        return Math.Abs(endAddress.Value - startAddress.Value) >= minRangeWidth;
    }

    protected bool ScanForValidationErrors(params string[] properties) {
        foreach (string property in properties) {
            _validationErrors.TryGetValue(property, out List<string>? values);
            if (values?.Count > 0) {
                return true;
            }
        }
        return false;
    }

    protected void ValidateAddressRange(State state, string? startAddress,
        string? endAddress, uint minRangeWidth, string textBoxBindedPropertyName) {
        const string RangeError = "Invalid address range.";
        const string StartError = "Invalid start address.";
        const string EndError = "Invalid end address.";
        bool rangeStatus = false;
        bool statusStart = AddressAndValueParser.TryValidateAddress(startAddress, state, out _);
        bool statusEnd = AddressAndValueParser.TryValidateAddress(endAddress, state, out _);
        if (statusStart && statusEnd) {
            if (AddressAndValueParser.TryParseAddressString(startAddress, state, out uint? start) &&
                AddressAndValueParser.TryParseAddressString(endAddress, state, out uint? end)) {
                rangeStatus = GetIsMemoryRangeValid(start, end, minRangeWidth);
            }
        }
        if (!_validationErrors.TryGetValue(textBoxBindedPropertyName,
        out List<string>? values)) {
            values = new List<string>();
            _validationErrors[textBoxBindedPropertyName] = values;
        } else {
            values.Clear();
        }
        if (!rangeStatus) {
            values.Add(RangeError);
        } else if (!statusStart) {
            values.Add(StartError);
        } else if (!statusEnd) {
            values.Add(EndError);
        }
        OnErrorsChanged(textBoxBindedPropertyName);
    }


    protected void ValidateAddressProperty(object? value, State state, [CallerMemberName]
        string? propertyName = null) {
        if (string.IsNullOrWhiteSpace(propertyName)) {
            return;
        }

        bool status = AddressAndValueParser.TryValidateAddress(value as string, state, out string? error);
        if (!status) {
            if (!_validationErrors.TryGetValue(propertyName,
                out List<string>? values)) {
                _validationErrors[propertyName] = [error];
            } else {
                values.Clear();
                values.Add(error);
            }
        } else {
            _validationErrors.Remove(propertyName);
        }
        OnErrorsChanged(propertyName);
    }

    protected void ValidateHexProperty(object? value, int length, [CallerMemberName] string? propertyName = null) {
        if (string.IsNullOrWhiteSpace(propertyName)) {
            return;
        }

        string? valueAsString = value as string;
        // Always remove any existing validation errors first
        _validationErrors.Remove(propertyName);

        if (string.IsNullOrWhiteSpace(valueAsString)) {
            // Value is empty, which is valid for optional fields - trigger error changed event
            OnErrorsChanged(propertyName);
            return;
        }
        if (!AddressAndValueParser.IsValidHex(valueAsString)) {
            _validationErrors[propertyName] = ["Invalid hex value"];
        } else {
            string actualHex = valueAsString[2..];
            int expectedLength;
            if (actualHex.Length == 1 && length == 1) {
                // Handles 0x1 instead of forcing user to write 0x01
                expectedLength = 1;
            } else {
                expectedLength = length * 2;
            }

            if (expectedLength != actualHex.Length) {
                _validationErrors[propertyName] = ["Invalid length"];
            }
        }
        OnErrorsChanged(propertyName);
    }
}