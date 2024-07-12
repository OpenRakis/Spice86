namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Core.Emulator.Memory;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.Shared.Diagnostics;
using Spice86.ViewModels.Messages;

using System.ComponentModel;

public partial class DebugWindowViewModel : ViewModelBase, IInternalDebugger {
    private readonly IDebuggableComponent _programExecutor;
    private readonly IHostStorageProvider _storageProvider;
    private readonly IUIDispatcherTimerFactory _uiDispatcherTimerFactory;
    private readonly ITextClipboard _textClipboard;
    private readonly IStructureViewModelFactory _structureViewModelFactory;
    private readonly IMessenger _messenger;

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
        _messenger = messenger;
        _messenger.Register<PauseStatusChangedMessage>(this, (_, message) => HandlePauseStatusChanged(message.IsPaused));
        _programExecutor = programExecutor;
        _structureViewModelFactory = structureViewModelFactory;
        _storageProvider = storageProvider;
        _textClipboard = textClipboard;
        _uiDispatcherTimerFactory = uiDispatcherTimerFactory;
        IsPaused = programExecutor.IsPaused;
        uiDispatcherTimerFactory.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateValues);
        var disassemblyVm = new DisassemblyViewModel(_messenger, programExecutor.IsPaused, false, uiDispatcherTimerFactory);
        DisassemblyViewModels.Add(disassemblyVm);
        PaletteViewModel = new(uiDispatcherTimerFactory);
        SoftwareMixerViewModel = new(uiDispatcherTimerFactory);
        VideoCardViewModel = new(uiDispatcherTimerFactory);
        CpuViewModel = new(_messenger, uiDispatcherTimerFactory);
        MidiViewModel = new(uiDispatcherTimerFactory);
        MemoryViewModels.Add(new(this, _messenger, textClipboard, uiDispatcherTimerFactory, storageProvider, programExecutor.IsPaused, false, 0, A20Gate.EndOfHighMemoryArea, _structureViewModelFactory));
        CfgCpuViewModel = new(_messenger, uiDispatcherTimerFactory, new PerformanceMeasurer());
        Dispatcher.UIThread.Post(ForceUpdate, DispatcherPriority.Background);
        _messenger.Register<AddViewModelMessage<MemoryViewModel>>(this, (_, _) => NewMemoryViewCommand.Execute(null));
        _messenger.Register<AddViewModelMessage<DisassemblyViewModel>>(this, (_, _) => NewDisassemblyViewCommand.Execute(null));
        _messenger.Register<RemoveViewModelMessage<DisassemblyViewModel>>(this, (_, message) => CloseTab(message.Sender));
        _messenger.Register<RemoveViewModelMessage<MemoryViewModel>>(this, (_, message) => CloseTab(message.Sender));
    }

    private void HandlePauseStatusChanged(bool isPaused) => IsPaused = isPaused;

    private void NotifyViaMessageAboutPauseStatus(bool isPaused) => _messenger.Send(new PauseStatusChangedMessage(isPaused));

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
    public void NewMemoryView() =>
        MemoryViewModels.Add(new MemoryViewModel(this, _messenger, _textClipboard, _uiDispatcherTimerFactory, _storageProvider, IsPaused, true, 0, A20Gate.EndOfHighMemoryArea, _structureViewModelFactory));
    
    [RelayCommand(CanExecute = nameof(IsPaused))]
    public void NewDisassemblyView() =>
        DisassemblyViewModels.Add(new DisassemblyViewModel(_messenger, true, IsPaused, _uiDispatcherTimerFactory));

    [RelayCommand]
    public void Pause() {
        IsPaused = true;
        NotifyViaMessageAboutPauseStatus(IsPaused);
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    public void Continue() {
        IsPaused = false;
        NotifyViaMessageAboutPauseStatus(IsPaused);
    }

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