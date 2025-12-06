namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.ViewModels.Services;
using Spice86.ViewModels.ValueViewModels.Debugging;
using Spice86.Views.Behaviors;

using System.Text.Json;

public abstract partial class ViewModelWithErrorDialog : ViewModelBase {
    protected readonly ITextClipboard _textClipboard;
    protected readonly IUIDispatcher _uiDispatcher;

    protected ViewModelWithErrorDialog(IUIDispatcher uiDispatcher,
        ITextClipboard textClipboard) {
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
        if (Exception is not null) {
            await _textClipboard.SetTextAsync(
                JsonSerializer.Serialize(
                    new ExceptionInfo(Exception.TargetSite?.ToString(), Exception.Message, Exception.StackTrace)));
        }
    }
}