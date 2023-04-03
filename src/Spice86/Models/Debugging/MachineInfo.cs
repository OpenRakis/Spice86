namespace Spice86.Models.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

public partial class MachineInfo : ObservableObject {
    [ObservableProperty]
    private byte _videoBiosInt10HandlerIndex;

    [ObservableProperty]
    private ushort? _videoBiosInt10HandlerInterruptHandlerSegment;
}