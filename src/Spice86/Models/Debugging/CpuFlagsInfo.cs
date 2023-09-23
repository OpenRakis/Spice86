namespace Spice86.Models.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using System.ComponentModel;

public partial class CpuFlagsInfo : ObservableObject {
    [ObservableProperty, Category("Flags")] private bool _overflowFlag;
    [ObservableProperty, Category("Flags")] private bool _parityFlag;
    [ObservableProperty, Category("Flags")] private bool _auxiliaryFlag;
    [ObservableProperty, Category("Flags")] private bool _carryFlag;
    [ObservableProperty, Category("Flags")] private bool? _continueZeroFlag;
    [ObservableProperty, Category("Flags")] private bool _directionFlag;
    [ObservableProperty, Category("Flags")] private bool _interruptFlag;
    [ObservableProperty, Category("Flags")] private bool _signFlag;
    [ObservableProperty, Category("Flags")] private bool _trapFlag;
    [ObservableProperty, Category("Flags")] private bool _zeroFlag;
    
}