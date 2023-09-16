namespace Spice86.Models.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using System.ComponentModel;

public partial class StateInfo : ObservableObject {
    // Accumulator
    [ObservableProperty] private string? _AH;
    [ObservableProperty] private string? _AL;
    [ObservableProperty] private bool _auxiliaryFlag;
    [ObservableProperty] private string? _AX;

    // Base
    [ObservableProperty] private string? _BH;
    [ObservableProperty] private string? _BL;

    // Base Pointer
    [ObservableProperty] private string? _BP;
    [ObservableProperty] private string? _BX;
    [ObservableProperty] private bool _carryFlag;

    // Counter
    [ObservableProperty] private string? _CH;
    [ObservableProperty] private string? _CL;

    [ObservableProperty] private bool? _continueZeroFlagValue;

    // Code Segment
    [ObservableProperty] private string? _CS;
    [ObservableProperty] private string? _CX;

    [ObservableProperty] private long _cycles;

    // Data
    [ObservableProperty] private string? _DH;

    // Destination Index
    [ObservableProperty] private string? _DI;

    [ObservableProperty] private string? _direction16;

    [ObservableProperty] private string? _direction32;

    [ObservableProperty] private string? _direction8;
    [ObservableProperty] private bool _directionFlag;
    [ObservableProperty] private string? _DL;

    // Data Segment
    [ObservableProperty] private string? _DS;
    [ObservableProperty] private string? _DX;
    [ObservableProperty] private string? _EAX;
    [ObservableProperty] private string? _EBP;
    [ObservableProperty] private string? _EBX;
    [ObservableProperty] private string? _ECX;
    [ObservableProperty] private string? _EDI;
    [ObservableProperty] private string? _EDX;

    // Extra segments
    [ObservableProperty] private string? _ES;
    [ObservableProperty] private string? _ESI;
    [ObservableProperty] private string? _ESP;

    [ObservableProperty] private string? _flags;
    [ObservableProperty] private string? _FS;
    [ObservableProperty] private string? _GS;
    [ObservableProperty] private bool _interruptFlag;

    /// <summary> Instruction pointer </summary>
    [ObservableProperty] private string? _IP;

    [ObservableProperty] private string? _ipPhysicalAddress;

    [ObservableProperty] private bool _isRunning;

    [ObservableProperty] private bool _overflowFlag;
    [ObservableProperty] private bool _parityFlag;

    [ObservableProperty] private string? _segmentOverrideIndex;

    // Source Index
    [ObservableProperty] private string? _SI;
    [ObservableProperty] private bool _signFlag;

    // Stack Pointer
    [ObservableProperty] private string? _SP;

    // Stack Segment
    [ObservableProperty] private string? _SS;

    [ObservableProperty] private string? _stackPhysicalAddress;
    [ObservableProperty] private bool _trapFlag;
    [ObservableProperty] private bool _zeroFlag;
}