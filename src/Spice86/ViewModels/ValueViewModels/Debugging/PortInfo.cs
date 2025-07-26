namespace Spice86.ViewModels.ValueViewModels.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using System.ComponentModel;

public partial class PortInfo : ObservableObject {
    [ObservableProperty, ReadOnly(true)] private int _lastPortRead;
    [ObservableProperty, ReadOnly(true)] private int _lastPortWritten;
    [ObservableProperty, ReadOnly(true)] private int _lastPortWrittenValue;
}