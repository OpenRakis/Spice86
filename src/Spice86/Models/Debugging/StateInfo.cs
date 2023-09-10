namespace Spice86.Models.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using System.ComponentModel;

public partial class StateInfo : ObservableObject {
    // Accumulator
    [ObservableProperty] [ReadOnly(true)] private string? _AH;
    [ObservableProperty] [ReadOnly(true)] private string? _AL;
    [ObservableProperty] [ReadOnly(true)] private bool _auxiliaryFlag;
    [ObservableProperty] [ReadOnly(true)] private string? _AX;

    // Base
    [ObservableProperty] [ReadOnly(true)] private string? _BH;
    [ObservableProperty] [ReadOnly(true)] private string? _BL;

    // Base Pointer
    [ObservableProperty] [ReadOnly(true)] private string? _BP;
    [ObservableProperty] [ReadOnly(true)] private string? _BX;
    [ObservableProperty] [ReadOnly(true)] private bool _carryFlag;

    // Counter
    [ObservableProperty] [ReadOnly(true)] private string? _CH;
    [ObservableProperty] [ReadOnly(true)] private string? _CL;

    [ObservableProperty] [ReadOnly(true)] private bool? _continueZeroFlagValue;

    // Code Segment
    [ObservableProperty] [ReadOnly(true)] private string? _CS;
    [ObservableProperty] [ReadOnly(true)] private string? _CX;

    [ObservableProperty] [ReadOnly(true)] private long _cycles;

    // Data
    [ObservableProperty] [ReadOnly(true)] private string? _DH;

    // Destination Index
    [ObservableProperty] [ReadOnly(true)] private string? _DI;

    [ObservableProperty] [ReadOnly(true)] private string? _direction16;

    [ObservableProperty] [ReadOnly(true)] private string? _direction32;

    [ObservableProperty] [ReadOnly(true)] private string? _direction8;
    [ObservableProperty] [ReadOnly(true)] private bool _directionFlag;
    [ObservableProperty] [ReadOnly(true)] private string? _DL;

    // Data Segment
    [ObservableProperty] [ReadOnly(true)] private string? _DS;
    [ObservableProperty] [ReadOnly(true)] private string? _DX;
    [ObservableProperty] [ReadOnly(true)] private string? _EAX;
    [ObservableProperty] [ReadOnly(true)] private string? _EBP;
    [ObservableProperty] [ReadOnly(true)] private string? _EBX;
    [ObservableProperty] [ReadOnly(true)] private string? _ECX;
    [ObservableProperty] [ReadOnly(true)] private string? _EDI;
    [ObservableProperty] [ReadOnly(true)] private string? _EDX;

    // Extra segments
    [ObservableProperty] [ReadOnly(true)] private string? _ES;
    [ObservableProperty] [ReadOnly(true)] private string? _ESI;
    [ObservableProperty] [ReadOnly(true)] private string? _ESP;

    [ObservableProperty] [ReadOnly(true)] private string? _flags;
    [ObservableProperty] [ReadOnly(true)] private string? _FS;
    [ObservableProperty] [ReadOnly(true)] private string? _GS;
    [ObservableProperty] [ReadOnly(true)] private bool _interruptFlag;

    /// <summary> Instruction pointer </summary>
    [ObservableProperty] [ReadOnly(true)] private string? _IP;

    [ObservableProperty] [ReadOnly(true)] private string? _ipPhysicalAddress;

    [ObservableProperty] [ReadOnly(true)] private bool _isRunning;

    [ObservableProperty] [ReadOnly(true)] private bool _overflowFlag;
    [ObservableProperty] [ReadOnly(true)] private bool _parityFlag;

    [ObservableProperty] [ReadOnly(true)] private string? _segmentOverrideIndex;

    // Source Index
    [ObservableProperty] [ReadOnly(true)] private string? _SI;
    [ObservableProperty] [ReadOnly(true)] private bool _signFlag;

    // Stack Pointer
    [ObservableProperty] [ReadOnly(true)] private string? _SP;

    // Stack Segment
    [ObservableProperty] [ReadOnly(true)] private string? _SS;

    [ObservableProperty] [ReadOnly(true)] private string? _stackPhysicalAddress;
    [ObservableProperty] [ReadOnly(true)] private bool _trapFlag;
    [ObservableProperty] [ReadOnly(true)] private bool _zeroFlag;
}