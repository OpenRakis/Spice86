namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public partial class ViewModelBase : ObservableObject {

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
}
