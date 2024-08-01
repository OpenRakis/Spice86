namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Core.Emulator.VM;
using Spice86.Infrastructure;
using Spice86.Messages;
using Spice86.Shared.Diagnostics;

public partial class DebugWindowViewModel : ViewModelBase, IInternalDebugger,
    IRecipient<AddViewModelMessage<DisassemblyViewModel>>, IRecipient<AddViewModelMessage<MemoryViewModel>>,
    IRecipient<RemoveViewModelMessage<DisassemblyViewModel>>, IRecipient<RemoveViewModelMessage<MemoryViewModel>> {
    private readonly IProgramExecutor _programExecutor;

    [ObservableProperty]
    private DateTime? _lastUpdate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
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

    public DebugWindowViewModel(IMessenger messenger, ITextClipboard textClipboard, IHostStorageProvider storageProvider, IUIDispatcherTimerFactory uiDispatcherTimerFactory, IProgramExecutor programExecutor, IStructureViewModelFactory structureViewModelFactory, IPauseHandler pauseHandler) {
        _programExecutor = programExecutor;
        messenger.Register<AddViewModelMessage<DisassemblyViewModel>>(this);
        messenger.Register<AddViewModelMessage<MemoryViewModel>>(this);
        messenger.Register<RemoveViewModelMessage<DisassemblyViewModel>>(this);
        messenger.Register<RemoveViewModelMessage<MemoryViewModel>>(this);
        _pauseHandler = pauseHandler;
        uiDispatcherTimerFactory.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateValues);
        var disassemblyVm = new DisassemblyViewModel(pauseHandler, messenger, uiDispatcherTimerFactory);
        DisassemblyViewModels.Add(disassemblyVm);
        PaletteViewModel = new(uiDispatcherTimerFactory);
        SoftwareMixerViewModel = new(uiDispatcherTimerFactory);
        VideoCardViewModel = new(uiDispatcherTimerFactory);
        CpuViewModel = new(pauseHandler, uiDispatcherTimerFactory);
        MidiViewModel = new(uiDispatcherTimerFactory);
        MemoryViewModels.Add(new(pauseHandler, messenger, textClipboard, uiDispatcherTimerFactory, storageProvider, structureViewModelFactory));
        CfgCpuViewModel = new(pauseHandler, uiDispatcherTimerFactory, new PerformanceMeasurer());
    }
    
    [RelayCommand]
    private void Pause() {
        _pauseHandler.RequestPause("Pause button pressed in debug window");
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void Continue() {
        _pauseHandler.Resume();
    }

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
    public void Receive(AddViewModelMessage<DisassemblyViewModel> message) => DisassemblyViewModels.Add(message.ViewModel);
    public void Receive(AddViewModelMessage<MemoryViewModel> message) => MemoryViewModels.Add(message.ViewModel);
    public void Receive(RemoveViewModelMessage<DisassemblyViewModel> message) => DisassemblyViewModels.Remove(message.ViewModel);
    public void Receive(RemoveViewModelMessage<MemoryViewModel> message) => MemoryViewModels.Remove(message.ViewModel);
}