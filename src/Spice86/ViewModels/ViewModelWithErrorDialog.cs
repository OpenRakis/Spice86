namespace Spice86.ViewModels;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Behaviors;
using Spice86.Infrastructure;
using Spice86.Models.Debugging;

public abstract partial class ViewModelWithErrorDialog : ViewModelBase {
    protected readonly ITextClipboard _textClipboard;
    
    protected ViewModelWithErrorDialog(ITextClipboard textClipboard) => _textClipboard = textClipboard;
    
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
        if(Exception is not null) {
            await _textClipboard.SetTextAsync(
                Newtonsoft.Json.JsonConvert.SerializeObject(
                    new ExceptionInfo(Exception.TargetSite?.ToString(), Exception.Message, Exception.StackTrace)));
        }
    }
}
