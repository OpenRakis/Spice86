namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Core.Emulator.Memory;
using Spice86.Infrastructure;
using Spice86.Interfaces;

public partial class DebugWindowViewModel : ViewModelBase, IInternalDebugger, IDebugViewModel {
    private readonly IPauseStatus? _pauseStatus;

    [ObservableProperty]
    private DateTime? _lastUpdate;
    
    private IMemory? _memory;
    
    [ObservableProperty]
    private MixerViewModel? _softwareMixerViewModel;

    public DebugWindowViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    [RelayCommand]
    public void ForceUpdate() {
        UpdateValues(this, EventArgs.Empty);
    }

    private IProgramExecutor? _programExecutor;

    public IProgramExecutor? ProgramExecutor {
        get => _programExecutor;
        set {
            if (value is null || _uiDispatcherTimerFactory is null) {
                return;
            }
            _programExecutor = value;
            PaletteViewModel = new(_uiDispatcherTimerFactory, value);
            if (_pauseStatus is not null) {
                DisassemblyViewModel = new(value, _pauseStatus);
            }

        }
    }

    [ObservableProperty]
    private int _selectedTab;

    private readonly IUIDispatcherTimerFactory? _uiDispatcherTimerFactory;

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

    public DebugWindowViewModel(IUIDispatcherTimerFactory uiDispatcherTimerFactory, IPauseStatus pauseStatus) {
        _pauseStatus = pauseStatus;
        uiDispatcherTimerFactory.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateValues);
        _uiDispatcherTimerFactory = uiDispatcherTimerFactory;
        SoftwareMixerViewModel = new(uiDispatcherTimerFactory);
        VideoCardViewModel = new();
        CpuViewModel = new(pauseStatus);
        MidiViewModel = new();
    }

    private void UpdateValues(object? sender, EventArgs e) {
        ProgramExecutor?.Accept(this);
        LastUpdate = DateTime.Now;
    }

    public void Visit<T>(T component) where T : IDebuggableComponent {
        DisassemblyViewModel?.Visit(component);
        CpuViewModel?.Visit(component);
        VideoCardViewModel?.Visit(component);
        MidiViewModel?.Visit((component));
        
        if (component is SoftwareMixer softwareMixer) {
            VisitSoundMixer(softwareMixer);
        }
        if(component is IMemory memory) {
            VisitMemory(memory);
        }
    }

    private void VisitMemory(IMemory memory) {
        if(_pauseStatus is null) {
            return;
        }
        _memory ??= memory;
        MemoryViewModel ??= new(memory, _pauseStatus);
    }

    public void ShowColorPalette() {
        SelectedTab = 4;
    }

    public void VisitSoundMixer(SoftwareMixer mixer) {
        SoftwareMixerViewModel?.VisitSoundMixer(mixer);
    }
}