namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.Shared.Diagnostics;

using System.ComponentModel;

public partial class DebugWindowViewModel : ViewModelBase, IInternalDebugger {
    private readonly IPauseStatus? _pauseStatus;
    private readonly IProgramExecutor? _programExecutor;

    [ObservableProperty]
    private DateTime? _lastUpdate;
    
    [ObservableProperty]
    private int _selectedTab;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private PaletteViewModel? _paletteViewModel;

    [ObservableProperty]
    private MemoryViewModel? _memoryViewModel;
    
    [ObservableProperty]
    private VideoCardViewModel? _videoCardViewModel;

    [ObservableProperty]
    private CpuViewModel? _cpuViewModel;

    [ObservableProperty]
    private MidiViewModel? _midiViewModel;

    [ObservableProperty]
    private DisassemblyViewModel? _disassemblyViewModel;
    
    [ObservableProperty]
    private SoftwareMixerViewModel? _softwareMixerViewModel;

    [ObservableProperty]
    private CfgCpuViewModel? _cfgCpuViewModel;

    public DebugWindowViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }
    
    public DebugWindowViewModel(IUIDispatcherTimerFactory uiDispatcherTimerFactory, IPauseStatus pauseStatus, IProgramExecutor programExecutor) {
        _programExecutor = programExecutor;
        _pauseStatus = pauseStatus;
        IsPaused = _programExecutor.IsPaused;
        _pauseStatus.PropertyChanged += OnPauseStatusChanged;
        uiDispatcherTimerFactory.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateValues);
        DisassemblyViewModel = new(pauseStatus);
        PaletteViewModel = new();
        SoftwareMixerViewModel = new();
        VideoCardViewModel = new();
        CpuViewModel = new(pauseStatus);
        MidiViewModel = new();
        MemoryViewModel = new(pauseStatus);
        CfgCpuViewModel = new(uiDispatcherTimerFactory, new PerformanceMeasurer(), pauseStatus);
        Dispatcher.UIThread.Post(() => programExecutor.Accept(this), DispatcherPriority.Background);
    }
    
    [RelayCommand]
    public void Pause() {
        if (_programExecutor is null || _pauseStatus is null) {
            return;
        }
        _pauseStatus.IsPaused = _programExecutor.IsPaused = IsPaused = true;
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    public void Continue() {
        if (_programExecutor is null || _pauseStatus is null) {
            return;
        }
        _pauseStatus.IsPaused = _programExecutor.IsPaused = IsPaused = false;
    }
    
    private void OnPauseStatusChanged(object? sender, PropertyChangedEventArgs e) => IsPaused = _pauseStatus?.IsPaused is true;

    [RelayCommand]
    public void ForceUpdate() {
        UpdateValues(this, EventArgs.Empty);
    }

    private void UpdateValues(object? sender, EventArgs e) {
        _programExecutor?.Accept(this);
    }

    public void Visit<T>(T component) where T : IDebuggableComponent {
        PaletteViewModel?.Visit(component);
        DisassemblyViewModel?.Visit(component);
        CpuViewModel?.Visit(component);
        VideoCardViewModel?.Visit(component);
        MidiViewModel?.Visit((component));
        SoftwareMixerViewModel?.Visit(component);
        MemoryViewModel?.Visit(component);
        CfgCpuViewModel?.Visit(component);
        LastUpdate = DateTime.Now;
    }

    public void ShowColorPalette() {
        SelectedTab = 4;
    }
}