namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Behaviors;
using Spice86.Infrastructure;
using Spice86.Models.Debugging;
using Spice86.Shared.Utils;

using System.Diagnostics.CodeAnalysis;

using System.Globalization;
using System.Text.Json;

public abstract partial class ViewModelWithErrorDialog : ViewModelBase {
    protected readonly ITextClipboard _textClipboard;
    protected readonly IUIDispatcher _uiDispatcher;

    protected ViewModelWithErrorDialog(IUIDispatcher uiDispatcher, ITextClipboard textClipboard) {
        _uiDispatcher = uiDispatcher;
        _textClipboard = textClipboard;
    }

    [RelayCommand]
    public void ClearDialog() => IsDialogVisible = false;

    [RelayCommand]
    public void ShowInternalDebugger(object? commandParameter) {
        if (commandParameter is ShowInternalDebuggerBehavior showInternalDebuggerBehavior) {
            showInternalDebuggerBehavior.ShowInternalDebugger();
        }
    }


    protected bool TryParseMemoryAddress(string? memoryAddress, [NotNullWhen(true)] out ulong? address) {
        if (string.IsNullOrWhiteSpace(memoryAddress)) {
            address = null;
            return false;
        }

        try {
            if (memoryAddress.Contains(':')) {
                string[] split = memoryAddress.Split(":");
                if (split.Length > 1 &&
                    ushort.TryParse(split[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort segment) &&
                    ushort.TryParse(split[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort offset)) {
                    address = MemoryUtils.ToPhysicalAddress(segment, offset);

                    return true;
                }
            } else if (ulong.TryParse(memoryAddress, CultureInfo.InvariantCulture, out ulong value)) {
                address = value;

                return true;
            }
        } catch (Exception e) {
            _uiDispatcher.Post(() => ShowError(e));
        }
        address = null;

        return false;
    }
    protected void ShowError(Exception e) {
        Exception = e.GetBaseException();
        IsDialogVisible = true;
    }
    [ObservableProperty]
    private bool _isDialogVisible;

    [ObservableProperty]
    private Exception? _exception;
    
    [RelayCommand]
    public async Task CopyExceptionToClipboard() {
        if(Exception is not null) {
            await _textClipboard.SetTextAsync(
                JsonSerializer.Serialize(
                    new ExceptionInfo(Exception.TargetSite?.ToString(), Exception.Message, Exception.StackTrace)));
        }
    }
}
