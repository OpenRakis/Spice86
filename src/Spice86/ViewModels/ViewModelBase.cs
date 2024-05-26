namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Infrastructure;
using Spice86.Models.Debugging;

public partial class ViewModelBase : ObservableObject {
    protected readonly ITextClipboard? _textClipboard;

    public ViewModelBase() { }

    public ViewModelBase(ITextClipboard? textClipboard) => _textClipboard = textClipboard;
    
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
    public async Task CopyToClipboard() {
        if(Exception is not null && _textClipboard is not null) {
            await _textClipboard.SetTextAsync(
                Newtonsoft.Json.JsonConvert.SerializeObject(
                    new ExceptionInfo(Exception.TargetSite?.ToString(), Exception.Message, Exception.StackTrace)));
        }
    }
}
