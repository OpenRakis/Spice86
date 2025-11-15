namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// View model for the breakpoint creation dialog.
/// </summary>
public partial class BreakpointDialogViewModel : ViewModelBase {
    [ObservableProperty]
    private string _address;

    [ObservableProperty]
    private string? _condition;

    [ObservableProperty]
    private bool _dialogResult;

    /// <summary>
    /// Initializes a new instance of the <see cref="BreakpointDialogViewModel"/> class.
    /// </summary>
    /// <param name="address">The address where the breakpoint will be created.</param>
    public BreakpointDialogViewModel(SegmentedAddress address) {
        _address = address.ToString();
        _condition = string.Empty;
        _dialogResult = false;
    }

    [RelayCommand]
    private void Ok() {
        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel() {
        DialogResult = false;
    }
}
