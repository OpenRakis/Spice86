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

public partial class DebugViewModel : ViewModelBase, IInternalDebugger {
    private readonly IPauseStatus? _pauseStatus;
    private readonly IProgramExecutor? _programExecutor;
    private readonly IHostStorageProvider? _storageProvider;
    private readonly IUIDispatcherTimerFactory? _uiDispatcherTimerFactory;

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
    private AvaloniaList<DisassemblyViewModel> _disassemblyViewModels = new();
    
    [ObservableProperty]
    private SoftwareMixerViewModel? _softwareMixerViewModel;

    public DebugViewModel(ITextClipboard textClipboard) : base(textClipboard) {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }
    
    public DebugViewModel(IHostStorageProvider storageProvider, IUIDispatcherTimerFactory uiDispatcherTimerFactory, IPauseStatus pauseStatus, IProgramExecutor programExecutor, ITextClipboard? textClipboard) : base() {
        _programExecutor = programExecutor;
        _storageProvider = storageProvider;
        _uiDispatcherTimerFactory = uiDispatcherTimerFactory;
        _pauseStatus = pauseStatus;
        IsPaused = _programExecutor.IsPaused;
        _pauseStatus.PropertyChanged += OnPauseStatusChanged;
        uiDispatcherTimerFactory.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateValues);
        var disassemblyVm = new DisassemblyViewModel(uiDispatcherTimerFactory, pauseStatus);
        DisassemblyViewModels.Add(disassemblyVm);
        PaletteViewModel = new(uiDispatcherTimerFactory);
        SoftwareMixerViewModel = new(uiDispatcherTimerFactory);
        VideoCardViewModel = new(uiDispatcherTimerFactory);
        CpuViewModel = new(uiDispatcherTimerFactory, pauseStatus);
        MidiViewModel = new(uiDispatcherTimerFactory);
        MemoryViewModels.Add( new(uiDispatcherTimerFactory, storageProvider, pauseStatus, textClipboard, 0));
        Dispatcher.UIThread.Post(ForceUpdate, DispatcherPriority.Background);
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    public void NewMemoryView() {
        if (_pauseStatus is not null && _storageProvider is not null && _uiDispatcherTimerFactory is not null) {
            MemoryViewModels.Add(new MemoryViewModel(_uiDispatcherTimerFactory, _storageProvider, _pauseStatus, _textClipboard, 0));
        }
    }
    
    [RelayCommand(CanExecute = nameof(IsPaused))]
    public void NewDisassemblyView() {
        if (_pauseStatus is not null && _uiDispatcherTimerFactory is not null) {
            DisassemblyViewModels.Add(new DisassemblyViewModel(_uiDispatcherTimerFactory, _pauseStatus));
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
    
    private IEnumerable<IInternalDebugger?> InternalDebuggers => new IInternalDebugger?[] {
        PaletteViewModel, CpuViewModel, VideoCardViewModel, MidiViewModel, SoftwareMixerViewModel
    }
        .Concat(DisassemblyViewModels)
        .Concat(MemoryViewModels);

    public void Visit<T>(T component) where T : IDebuggableComponent {
        if (NeedsToVisitEmulator) {
            foreach (IInternalDebugger? debugger in InternalDebuggers.Where(x => x?.NeedsToVisitEmulator is true)) {
                debugger?.Visit(component);
            }
        }
        LastUpdate = DateTime.Now;
    }

    public bool NeedsToVisitEmulator => InternalDebuggers.Any(x => x?.NeedsToVisitEmulator == true);

    public void ShowColorPalette() {
        SelectedTab = 4;
    }
}