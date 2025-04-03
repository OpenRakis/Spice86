namespace Spice86.ViewModels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public abstract partial class ValidatorViewModelBase : ViewModelBase, INotifyDataErrorInfo {
    protected readonly Dictionary<string, List<string>> _errors = new();
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
        if (!_errors.TryGetValue(bindedPropertyName, out List<string>? values)) {
            _errors.Add(bindedPropertyName, ["This field is required."]);
        } else {
            values.Clear();
            values.Add("This field is required.");
        }
        OnErrorsChanged(bindedPropertyName);
        validatedValue = default;
        return false;
    }
}
