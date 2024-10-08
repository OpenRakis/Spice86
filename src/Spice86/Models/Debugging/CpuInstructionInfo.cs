namespace Spice86.Models.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using Iced.Intel;

public partial class CpuInstructionInfo : ObservableObject {
    [ObservableProperty] private string? _stringRepresentation;
    [ObservableProperty] private bool _isCsIp;
    [ObservableProperty] private uint _address;
    [ObservableProperty] private string? _segmentedAddress;
    [ObservableProperty] private ushort _IP16;
    [ObservableProperty] private uint _IP32;
    [ObservableProperty] private int _length;
    [ObservableProperty] private Register _memorySegment;
    [ObservableProperty] private Register _segmentPrefix;
    [ObservableProperty] private bool _isStackInstruction;
    [ObservableProperty] private bool _isIPRelativeMemoryOperand;
    [ObservableProperty] private ulong _IPRelativeMemoryAddress;
    [ObservableProperty] private FlowControl _flowControl;
    [ObservableProperty] private Instruction _instruction;
    [ObservableProperty] private string? _bytes;
}