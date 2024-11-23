namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Infrastructure;
using Spice86.Models.Debugging;

using System.ComponentModel;
using System.Reflection;

public partial class CpuViewModel : ViewModelBase {
    private readonly State _cpuState;
    private readonly IMemory _memory;
    
    [ObservableProperty]
    private StateInfo _state = new();

    [ObservableProperty]
    private CpuFlagsInfo _flags = new();

    public CpuViewModel(State state, Stack stack, IMemory memory, IPauseHandler pauseHandler, IUIDispatcher uiDispatcher) {
        _cpuState = state;
        _memory = memory;
        pauseHandler.Pausing += () => uiDispatcher.Post(() => _isPaused = true);
        _isPaused = pauseHandler.IsPaused;
        pauseHandler.Resumed += () => uiDispatcher.Post(() => _isPaused = false);
        DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdateValues);
    }

    private void UpdateValues(object? sender, EventArgs e) {
        VisitCpuState(_cpuState);
    }
    
    private bool _isPaused;
    
    private void VisitCpuState(State state) {
        UpdateCpuState(state);

        if (_isPaused) {
            State.PropertyChanged += OnStatePropertyChanged;
            Flags.PropertyChanged += OnStatePropertyChanged;
        } else {
            State.PropertyChanged -= OnStatePropertyChanged;
            Flags.PropertyChanged -= OnStatePropertyChanged;
        }

        void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (sender is null || e.PropertyName == null || !_isPaused) {
                return;
            }
            PropertyInfo? originalPropertyInfo = state.GetType().GetProperty(e.PropertyName);
            PropertyInfo? propertyInfo = sender.GetType().GetProperty(e.PropertyName);
            if (propertyInfo is not null && originalPropertyInfo is not null && originalPropertyInfo.CanWrite) {
                originalPropertyInfo.SetValue(state, propertyInfo.GetValue(sender));
            }
        }
    }

    [ObservableProperty]
    private string? _esiString;

    [ObservableProperty]
    private string? _ediString;

    [ObservableProperty]
    private string? _espString;

    private void UpdateCpuState(State state) {
        State.AH = state.AH;
        State.AL = state.AL;
        State.AX = state.AX;
        State.EAX = state.EAX;
        State.BH = state.BH;
        State.BL = state.BL;
        State.BX = state.BX;
        State.EBX = state.EBX;
        State.CH = state.CH;
        State.CL = state.CL;
        State.CX = state.CX;
        State.ECX = state.ECX;
        State.DH = state.DH;
        State.DL = state.DL;
        State.DX = state.DX;
        State.EDX = state.EDX;
        State.DI = state.DI;
        State.EDI = state.EDI;
        State.SI = state.SI;
        State.ES = state.ES;
        State.BP = state.BP;
        State.EBP = state.EBP;
        State.SP = state.SP;
        State.ESP = state.ESP;
        State.CS = state.CS;
        State.DS = state.DS;
        State.ES = state.ES;
        State.FS = state.FS;
        State.GS = state.GS;
        State.SS = state.SS;
        State.IP = state.IP;
        EspString = _memory.GetZeroTerminatedString(State.ESP, 32);
        EsiString = _memory.GetZeroTerminatedString(State.ESI, 32);
        EdiString = _memory.GetZeroTerminatedString(State.EDI, 32);
        State.Cycles = state.Cycles;
        State.IpPhysicalAddress = state.IpPhysicalAddress;
        State.StackPhysicalAddress = state.StackPhysicalAddress;
        State.SegmentOverrideIndex = state.SegmentOverrideIndex;
        Flags.AuxiliaryFlag = state.AuxiliaryFlag;
        Flags.CarryFlag = state.CarryFlag;
        Flags.DirectionFlag = state.DirectionFlag;
        Flags.InterruptFlag = state.InterruptFlag;
        Flags.OverflowFlag = state.OverflowFlag;
        Flags.ParityFlag = state.ParityFlag;
        Flags.ZeroFlag = state.ZeroFlag;
        Flags.ContinueZeroFlag = state.ContinueZeroFlagValue;
    }
}