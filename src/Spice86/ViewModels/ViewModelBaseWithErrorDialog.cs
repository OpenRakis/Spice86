namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Infrastructure;
using Spice86.Models.Debugging;

using System.Diagnostics;

public partial class ViewModelBaseWithErrorDialog : ViewModelBase {
    protected readonly ITextClipboard _textClipboard;
    
    protected ViewModelBaseWithErrorDialog(ITextClipboard textClipboard) => _textClipboard = textClipboard;
    
    [RelayCommand]
    public void ClearDialog() => IsDialogVisible = false;

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
            Exception.Demystify();
            await _textClipboard.SetTextAsync(
                Newtonsoft.Json.JsonConvert.SerializeObject(
                    new ExceptionInfo(Exception.TargetSite?.ToString(), Exception.Message, Exception.StackTrace)));
        }
    }
}
