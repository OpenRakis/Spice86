namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.ViewModels.ValueViewModels.Debugging;
using Spice86.Shared.Utils;

using System.ComponentModel;
using System.Reflection;
using Spice86.ViewModels.PropertiesMappers;
using Spice86.ViewModels.Services;

public partial class CpuViewModel : ViewModelBase, IEmulatorObjectViewModel {
    private readonly State _cpuState;
    private readonly IMemory _memory;
    
    [ObservableProperty]
    private StateInfo _state = new();

    [ObservableProperty]
    private CpuFlagsInfo _flags = new();

    public CpuViewModel(State state, IMemory memory, IPauseHandler pauseHandler, IUIDispatcher uiDispatcher) {
        _cpuState = state;
        _memory = memory;
        pauseHandler.Paused += () => uiDispatcher.Post(() => _isPaused = true);
        _isPaused = pauseHandler.IsPaused;
        pauseHandler.Resumed += () => uiDispatcher.Post(() => _isPaused = false);
        DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, UpdateValues);
    }

    public bool IsVisible { get; set; }

    public void UpdateValues(object? sender, EventArgs e) {
        if (!IsVisible) {
            return;
        }
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
    private string? _esDiString;

    [ObservableProperty]
    private string? _dsSiString;

    [ObservableProperty]
    private string? _dsDxString;


    private void UpdateCpuState(State state) {
        state.CopyToStateInfo(this.State);
        state.CopyFlagsToStateInfo(this.Flags);
        EsDiString = _memory.GetZeroTerminatedString(
            MemoryUtils.ToPhysicalAddress(State.ES, State.DI),
            32);
        DsSiString = _memory.GetZeroTerminatedString(
            MemoryUtils.ToPhysicalAddress(State.DS, State.SI),
            32);
        DsDxString = _memory.GetZeroTerminatedString(
            MemoryUtils.ToPhysicalAddress(State.DS, State.DX),
            32);
    }
}