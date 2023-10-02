namespace Spice86.Models.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using System.ComponentModel;

public partial class MidiInfo : ObservableObject {
    [ObservableProperty, ReadOnly(true)] private int _lastPortRead;
    [ObservableProperty, ReadOnly(true)] private int _lastPortWritten;
    [ObservableProperty, ReadOnly(true)] private int _lastPortWrittenValue;
}