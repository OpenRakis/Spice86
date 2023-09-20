namespace Spice86.Models.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using System.ComponentModel;

public partial class StateInfo : ObservableObject {
    [ObservableProperty] private byte _AH;
    [ObservableProperty] private byte _AL;
    [ObservableProperty, Category("Flags")] private bool _auxiliaryFlag;
    [ObservableProperty] private ushort _AX;

    [ObservableProperty] private byte _BH;
    [ObservableProperty] private byte _BL;

    [ObservableProperty] private ushort _BP;
    [ObservableProperty] private ushort _BX;
    [ObservableProperty, Category("Flags")] private bool _carryFlag;

    [ObservableProperty] private byte _CH;
    [ObservableProperty] private byte _CL;

    [ObservableProperty, Category("Flags")] private bool? _continueZeroFlag;

    [ObservableProperty] private ushort _CS;
    [ObservableProperty] private ushort _CX;

    [ObservableProperty, ReadOnly(true)] private long _cycles;

    [ObservableProperty] private byte _DH;

    [ObservableProperty] private ushort _DI;

    [ObservableProperty] private short _direction16;

    [ObservableProperty] private short _direction32;

    [ObservableProperty] private short _direction8;
    [ObservableProperty, Category("Flags")] private bool _directionFlag;
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
    [ObservableProperty, Category("Flags")] private bool _interruptFlag;

    /// <summary> Instruction pointer </summary>
    [ObservableProperty] private ushort _IP;

    [ObservableProperty] private uint _ipPhysicalAddress;

    [ObservableProperty, ReadOnly(true)] private bool _isRunning;

    [ObservableProperty] private bool _overflowFlag;
    [ObservableProperty] private bool _parityFlag;

    [ObservableProperty] private int? _segmentOverrideIndex;

    // Source Index
    [ObservableProperty] private ushort _SI;
    [ObservableProperty, Category("Flags")] private bool _signFlag;

    // Stack Pointer
    [ObservableProperty] private ushort _SP;

    // Stack Segment
    [ObservableProperty] private ushort _SS;

    [ObservableProperty] private uint _stackPhysicalAddress;
    [ObservableProperty, Category("Flags")] private bool _trapFlag;
    [ObservableProperty, Category("Flags")] private bool _zeroFlag;
}