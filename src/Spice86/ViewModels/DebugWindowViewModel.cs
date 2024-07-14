namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Infrastructure;
using Spice86.Messages;
using Spice86.Shared.Diagnostics;

public partial class DebugWindowViewModel : ViewModelBase, IInternalDebugger, IRecipient<PauseChangedMessage> {
    private readonly IMessenger _messenger;
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

    public DebugWindowViewModel(IMessenger messenger, ITextClipboard textClipboard, IHostStorageProvider storageProvider, IUIDispatcherTimerFactory uiDispatcherTimerFactory, IProgramExecutor programExecutor, IStructureViewModelFactory structureViewModelFactory) {
        _programExecutor = programExecutor;
        _messenger = messenger;
        _messenger.Register(this);
        _structureViewModelFactory = structureViewModelFactory;
        _storageProvider = storageProvider;
        _textClipboard = textClipboard;
        _uiDispatcherTimerFactory = uiDispatcherTimerFactory;
        IsPaused = _programExecutor.IsPaused;
        uiDispatcherTimerFactory.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateValues);
        var disassemblyVm = new DisassemblyViewModel(messenger, this, uiDispatcherTimerFactory);
        DisassemblyViewModels.Add(disassemblyVm);
        PaletteViewModel = new(uiDispatcherTimerFactory);
        SoftwareMixerViewModel = new(uiDispatcherTimerFactory);
        VideoCardViewModel = new(uiDispatcherTimerFactory);
        CpuViewModel = new(messenger, uiDispatcherTimerFactory);
        MidiViewModel = new(uiDispatcherTimerFactory);
        MemoryViewModels.Add(new(messenger, this, textClipboard, uiDispatcherTimerFactory, storageProvider, 0, _structureViewModelFactory));
        CfgCpuViewModel = new(messenger, uiDispatcherTimerFactory, new PerformanceMeasurer());
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
    private void NewMemoryView() {
        MemoryViewModels.Add(new MemoryViewModel(_messenger, this, _textClipboard, _uiDispatcherTimerFactory, _storageProvider, 0, _structureViewModelFactory));
        _messenger.Send(new PauseChangedMessage(IsPaused: IsPaused));
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void NewDisassemblyView() {
        DisassemblyViewModels.Add(new DisassemblyViewModel(_messenger, this, _uiDispatcherTimerFactory));
        _messenger.Send(new PauseChangedMessage(IsPaused: IsPaused));
    }

    [RelayCommand]
    private void Pause() => _messenger.Send(new PauseChangedMessage(true));

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void Continue() => _messenger.Send(new PauseChangedMessage(false));

    public void Receive(PauseChangedMessage message) =>  IsPaused = message.IsPaused;

    [RelayCommand]
    private void ForceUpdate() => UpdateValues(this, EventArgs.Empty);

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