namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.Models.Debugging;

using System.ComponentModel;
using System.Reflection;

public partial class CpuViewModel : ViewModelBase, IInternalDebugger {
    private readonly IPauseStatus _pauseStatus;
    private State? _cpuState;
    
    [ObservableProperty]
    private StateInfo _state = new();

    [ObservableProperty]
    private CpuFlagsInfo _flags = new();

    public CpuViewModel(IUIDispatcherTimerFactory dispatcherTimerFactory, IPauseStatus pauseStatus) {
        _pauseStatus = pauseStatus;
        dispatcherTimerFactory.StartNew(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdateValues);
    }

    private void UpdateValues(object? sender, EventArgs e) {
        if (_cpuState is not null) {
            VisitCpuState(_cpuState);
        }
    }
    
    public bool NeedsToVisitEmulator => _cpuState is null;

    public void Visit<T>(T component) where T : IDebuggableComponent {
        _cpuState ??= component as State;
    }
    
    private bool IsPaused => _pauseStatus.IsPaused;
    
    private void VisitCpuState(State state) {
        UpdateCpuState(state);

        if (IsPaused) {
            State.PropertyChanged -= OnStatePropertyChanged;
            State.PropertyChanged += OnStatePropertyChanged;
            Flags.PropertyChanged -= OnStatePropertyChanged;
            Flags.PropertyChanged += OnStatePropertyChanged;
        } else {
            State.PropertyChanged -= OnStatePropertyChanged;
            Flags.PropertyChanged -= OnStatePropertyChanged;
        }

        return;

        void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (sender is null || e.PropertyName == null || !IsPaused) {
                return;
            }
            PropertyInfo? originalPropertyInfo = state.GetType().GetProperty(e.PropertyName);
            PropertyInfo? propertyInfo = sender.GetType().GetProperty(e.PropertyName);
            if (propertyInfo is not null && originalPropertyInfo is not null) {
                originalPropertyInfo.SetValue(state, propertyInfo.GetValue(sender));
            }
        }
    }
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