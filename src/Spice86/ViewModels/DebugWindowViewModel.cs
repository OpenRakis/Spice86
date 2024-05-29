namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Infrastructure;
using Spice86.Interfaces;

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
    [NotifyCanExecuteChangedFor(nameof(NewMemoryViewCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private PaletteViewModel? _paletteViewModel;

    [ObservableProperty]
    private AvaloniaList<MemoryViewModel> _memoryViewModels = new();
    
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

    public DebugWindowViewModel(ITextClipboard textClipboard) : base(textClipboard) {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }
    
    public DebugWindowViewModel(IUIDispatcherTimerFactory uiDispatcherTimerFactory, IPauseStatus pauseStatus, IProgramExecutor programExecutor, ITextClipboard? textClipboard) : base() {
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
        MemoryViewModels.Add( new(pauseStatus, textClipboard, 0));
        Dispatcher.UIThread.Post(() => programExecutor.Accept(this), DispatcherPriority.Background);
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    public void NewMemoryView() {
        if (_pauseStatus is not null) {
            MemoryViewModels.Add(new MemoryViewModel(_pauseStatus, _textClipboard, 0));
        }
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
        foreach (MemoryViewModel memViewModel in MemoryViewModels) {
            memViewModel.Visit(component);
        }
        LastUpdate = DateTime.Now;
    }

    public void ShowColorPalette() {
        SelectedTab = 4;
    }
}