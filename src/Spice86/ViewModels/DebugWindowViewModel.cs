namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Infrastructure;
using Spice86.Interfaces;

public partial class DebugWindowViewModel : ViewModelBase, IInternalDebugger {
    [ObservableProperty]
    private DateTime? _lastUpdate;
    
    [ObservableProperty]
    private int _selectedTab;

    private readonly IDebuggableComponent? _rootComponent;

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

    public DebugWindowViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    [RelayCommand]
    public void ForceUpdate() {
        UpdateValues(this, EventArgs.Empty);
    }
    
    public DebugWindowViewModel(IUIDispatcherTimerFactory uiDispatcherTimerFactory, IPauseStatus pauseStatus, IDebuggableComponent rootComponent) {
        _rootComponent = rootComponent;
        uiDispatcherTimerFactory.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateValues);
        DisassemblyViewModel = new(pauseStatus);
        PaletteViewModel = new();
        SoftwareMixerViewModel = new();
        VideoCardViewModel = new();
        CpuViewModel = new(pauseStatus);
        MidiViewModel = new();
        MemoryViewModel = new(pauseStatus);
        Dispatcher.UIThread.Post(() => rootComponent.Accept(this), DispatcherPriority.Background);
    }

    private void UpdateValues(object? sender, EventArgs e) {
        _rootComponent?.Accept(this);
    }

    public void Visit<T>(T component) where T : IDebuggableComponent {
        PaletteViewModel?.Visit(component);
        DisassemblyViewModel?.Visit(component);
        CpuViewModel?.Visit(component);
        VideoCardViewModel?.Visit(component);
        MidiViewModel?.Visit((component));
        SoftwareMixerViewModel?.Visit(component);
        MemoryViewModel?.Visit(component);
        LastUpdate = DateTime.Now;
    }

    public void ShowColorPalette() {
        SelectedTab = 4;
    }
}