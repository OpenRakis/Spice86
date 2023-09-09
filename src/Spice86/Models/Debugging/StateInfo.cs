namespace Spice86.Models.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

public partial class StateInfo : ObservableObject {
    // Accumulator
    [ObservableProperty, ReadOnly(true)] private string? _AH;
    [ObservableProperty, ReadOnly(true)] private string? _AL;
    [ObservableProperty, ReadOnly(true)] private string? _AX;
    [ObservableProperty, ReadOnly(true)] private string? _EAX;

    // Base
    [ObservableProperty, ReadOnly(true)] private string? _BH;
    [ObservableProperty, ReadOnly(true)] private string? _BL;
    [ObservableProperty, ReadOnly(true)] private string? _BX;
    [ObservableProperty, ReadOnly(true)] private string? _EBX;

    // Counter
    [ObservableProperty, ReadOnly(true)] private string? _CH;
    [ObservableProperty, ReadOnly(true)] private string? _CL;
    [ObservableProperty, ReadOnly(true)] private string? _CX;
    [ObservableProperty, ReadOnly(true)] private string? _ECX;

    // Data
    [ObservableProperty, ReadOnly(true)] private string? _DH;
    [ObservableProperty, ReadOnly(true)] private string? _DL;
    [ObservableProperty, ReadOnly(true)] private string? _DX;
    [ObservableProperty, ReadOnly(true)] private string? _EDX;

    // Destination Index
    [ObservableProperty, ReadOnly(true)] private string? _DI;
    [ObservableProperty, ReadOnly(true)] private string? _EDI;

    // Source Index
    [ObservableProperty, ReadOnly(true)] private string? _SI;
    [ObservableProperty, ReadOnly(true)] private string? _ESI;

    // Base Pointer
    [ObservableProperty, ReadOnly(true)] private string? _BP;
    [ObservableProperty, ReadOnly(true)] private string? _EBP;

    // Stack Pointer
    [ObservableProperty, ReadOnly(true)] private string? _SP;
    [ObservableProperty, ReadOnly(true)] private string? _ESP;

    // Code Segment
    [ObservableProperty, ReadOnly(true)] private string? _CS;

    // Data Segment
    [ObservableProperty, ReadOnly(true)] private string? _DS;

    // Extra segments
    [ObservableProperty, ReadOnly(true)] private string? _ES;
    [ObservableProperty, ReadOnly(true)] private string? _FS;
    [ObservableProperty, ReadOnly(true)] private string? _GS;

    // Stack Segment
    [ObservableProperty, ReadOnly(true)] private string? _SS;

    /// <summary> Instruction pointer </summary>
    [ObservableProperty, ReadOnly(true)] private string? _IP;

    [ObservableProperty, ReadOnly(true)] private string? _flags;

    [ObservableProperty, ReadOnly(true)] private bool _overflowFlag;
    [ObservableProperty, ReadOnly(true)] private bool _directionFlag;
    [ObservableProperty, ReadOnly(true)] private bool _interruptFlag;
    [ObservableProperty, ReadOnly(true)] private bool _trapFlag;
    [ObservableProperty, ReadOnly(true)] private bool _signFlag;
    [ObservableProperty, ReadOnly(true)] private bool _zeroFlag;
    [ObservableProperty, ReadOnly(true)] private bool _auxiliaryFlag;
    [ObservableProperty, ReadOnly(true)] private bool _parityFlag;
    [ObservableProperty, ReadOnly(true)] private bool _carryFlag;

    [ObservableProperty, ReadOnly(true)] private string? _direction8;

    [ObservableProperty, ReadOnly(true)] private string? _direction16;

    [ObservableProperty, ReadOnly(true)] private string? _direction32;

    [ObservableProperty, ReadOnly(true)] private bool? _continueZeroFlagValue;

    [ObservableProperty, ReadOnly(true)] private string? _segmentOverrideIndex;

    [ObservableProperty, ReadOnly(true)] private long _cycles;

    [ObservableProperty, ReadOnly(true)] private string? _ipPhysicalAddress;

    [ObservableProperty, ReadOnly(true)] private string? _stackPhysicalAddress;

    [ObservableProperty, ReadOnly(true)] private bool _isRunning;
}