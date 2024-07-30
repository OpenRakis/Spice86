namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Core.Emulator.VM;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.Shared.Diagnostics;

using System.ComponentModel;

public partial class DebugWindowViewModel : ViewModelBase, IInternalDebugger {
    private readonly IPauseStatus _pauseStatus;
    private readonly IProgramExecutor _programExecutor;
    private readonly IHostStorageProvider _storageProvider;
    private readonly IUIDispatcherTimerFactory _uiDispatcherTimerFactory;
    private readonly ITextClipboard _textClipboard;
    private readonly IStructureViewModelFactory _structureViewModelFactory;

    [ObservableProperty]
    private DateTime? _lastUpdate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewMemoryViewCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewDisassemblyViewCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private PaletteViewModel _paletteViewModel;

    [ObservableProperty]
    private AvaloniaList<MemoryViewModel> _memoryViewModels = new();

    [ObservableProperty]
    private VideoCardViewModel _videoCardViewModel;

    [ObservableProperty]
    private CpuViewModel _cpuViewModel;

    [ObservableProperty]
    private MidiViewModel _midiViewModel;

    [ObservableProperty]
    private AvaloniaList<DisassemblyViewModel> _disassemblyViewModels = new();

    [ObservableProperty]
    private SoftwareMixerViewModel _softwareMixerViewModel;

    [ObservableProperty]
    private CfgCpuViewModel _cfgCpuViewModel;

    private readonly IPauseHandler _pauseHandler;

    public DebugWindowViewModel(ITextClipboard textClipboard, IHostStorageProvider storageProvider, IUIDispatcherTimerFactory uiDispatcherTimerFactory, IPauseStatus pauseStatus, IProgramExecutor programExecutor, IStructureViewModelFactory structureViewModelFactory, IPauseHandler pauseHandler) {
        _programExecutor = programExecutor;
        _structureViewModelFactory = structureViewModelFactory;
        _storageProvider = storageProvider;
        _textClipboard = textClipboard;
        _uiDispatcherTimerFactory = uiDispatcherTimerFactory;
        _pauseStatus = pauseStatus;
        _pauseHandler = pauseHandler;
        _pauseStatus.PropertyChanged += OnPauseStatusChanged;
        uiDispatcherTimerFactory.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateValues);
        var disassemblyVm = new DisassemblyViewModel(this, uiDispatcherTimerFactory, pauseStatus);
        DisassemblyViewModels.Add(disassemblyVm);
        PaletteViewModel = new(uiDispatcherTimerFactory);
        SoftwareMixerViewModel = new(uiDispatcherTimerFactory);
        VideoCardViewModel = new(uiDispatcherTimerFactory);
        CpuViewModel = new(uiDispatcherTimerFactory, pauseStatus);
        MidiViewModel = new(uiDispatcherTimerFactory);
        MemoryViewModels.Add(new(this, textClipboard, uiDispatcherTimerFactory, storageProvider, pauseStatus, 0, _structureViewModelFactory));
        CfgCpuViewModel = new(uiDispatcherTimerFactory, new PerformanceMeasurer(), pauseStatus);
        Dispatcher.UIThread.Post(ForceUpdate, DispatcherPriority.Background);
    }

    internal void CloseTab(IInternalDebugger internalDebuggerViewModel) {
        switch (internalDebuggerViewModel) {
            case MemoryViewModel memoryViewModel:
                MemoryViewModels.Remove(memoryViewModel);

                break;
            case DisassemblyViewModel disassemblyViewModel:
                DisassemblyViewModels.Remove(disassemblyViewModel);

                break;
        }
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    public void NewMemoryView() {
        MemoryViewModels.Add(new MemoryViewModel(this, _textClipboard, _uiDispatcherTimerFactory, _storageProvider, _pauseStatus, 0, _structureViewModelFactory));
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    public void NewDisassemblyView() => DisassemblyViewModels.Add(new DisassemblyViewModel(this, _uiDispatcherTimerFactory, _pauseStatus));

    [RelayCommand]
    private void Pause() {
        _pauseHandler.RequestPause("Pause button pressed in debug window");
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void Continue() {
        _pauseHandler.Resume();
    }

    private void OnPauseStatusChanged(object? sender, PropertyChangedEventArgs e) => IsPaused = _pauseStatus.IsPaused;

    [RelayCommand]
    public void ForceUpdate() => UpdateValues(this, EventArgs.Empty);

    private void UpdateValues(object? sender, EventArgs e) => _programExecutor.Accept(this);

    private IEnumerable<IInternalDebugger> InternalDebuggers => new IInternalDebugger[] {
        PaletteViewModel, CpuViewModel, VideoCardViewModel, MidiViewModel, SoftwareMixerViewModel, CfgCpuViewModel
        }
        .Concat(DisassemblyViewModels)
        .Concat(MemoryViewModels);

    public void Visit<T>(T component) where T : IDebuggableComponent {
        if (NeedsToVisitEmulator) {
            foreach (IInternalDebugger debugger in InternalDebuggers.Where(x => x.NeedsToVisitEmulator)) {
                debugger.Visit(component);
            }
        }
        LastUpdate = DateTime.Now;
    }

    public bool NeedsToVisitEmulator => InternalDebuggers.Any(x => x.NeedsToVisitEmulator);
}