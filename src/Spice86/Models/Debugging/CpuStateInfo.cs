namespace Spice86.Models.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using System.ComponentModel;

public partial class StateInfo : ObservableObject {
    [ObservableProperty] private byte _AH;
    [ObservableProperty] private byte _AL;
    [ObservableProperty] private ushort _AX;

    [ObservableProperty] private byte _BH;
    [ObservableProperty] private byte _BL;

    [ObservableProperty] private ushort _BP;
    [ObservableProperty] private ushort _BX;

    [ObservableProperty] private byte _CH;
    [ObservableProperty] private byte _CL;


    [ObservableProperty] private ushort _CS;
    [ObservableProperty] private ushort _CX;

    [ObservableProperty, ReadOnly(true)] private long _cycles;

    [ObservableProperty] private byte _DH;

    [ObservableProperty] private ushort _DI;
    [ObservableProperty] private byte _DL;

    [ObservableProperty] private ushort _DS;
    [ObservableProperty] private ushort _DX;
    [ObservableProperty] private uint _EAX;
    [ObservableProperty] private uint _EBP;
    [ObservableProperty] private uint _EBX;
    [ObservableProperty] private uint _ECX;
    [ObservableProperty] private uint _EDI;
    [ObservableProperty] private uint _EDX;

    [ObservableProperty] private ushort _ES;
    [ObservableProperty] private uint _ESI;
    [ObservableProperty] private uint _ESP;

    [ObservableProperty] private ushort _FS;
    [ObservableProperty] private ushort _GS;

    /// <summary> Instruction pointer </summary>
    [ObservableProperty] private ushort _IP;

    [ObservableProperty, ReadOnly(true)] private uint _ipPhysicalAddress;

    [ObservableProperty] private int? _segmentOverrideIndex;

    // Source Index
    [ObservableProperty] private ushort _SI;

    // Stack Pointer
    [ObservableProperty] private ushort _SP;

    // Stack Segment
    [ObservableProperty] private ushort _SS;

    [ObservableProperty, ReadOnly(true)] private uint _stackPhysicalAddress;
}