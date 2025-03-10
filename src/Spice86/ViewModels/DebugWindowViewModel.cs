namespace Spice86.ViewModels;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.VM;
using Spice86.Infrastructure;
using Spice86.Messages;

public partial class DebugWindowViewModel : ViewModelBase,
    IRecipient<AddViewModelMessage<DisassemblyViewModel>>, IRecipient<AddViewModelMessage<MemoryViewModel>>,
    IRecipient<RemoveViewModelMessage<DisassemblyViewModel>>, IRecipient<RemoveViewModelMessage<MemoryViewModel>>,
    IRecipient<AddViewModelMessage<ModernDisassemblyViewModel>>, IRecipient<RemoveViewModelMessage<ModernDisassemblyViewModel>> {

    private readonly IMessenger _messenger;
    private readonly IUIDispatcher _uiDispatcher;

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
    private AvaloniaList<ModernDisassemblyViewModel> _modernDisassemblyViewModels = new();

    [ObservableProperty]
    private SoftwareMixerViewModel _softwareMixerViewModel;

    [ObservableProperty]
    private CfgCpuViewModel _cfgCpuViewModel;

    [ObservableProperty]
    private StatusMessageViewModel _statusMessageViewModel;

    [ObservableProperty]
    private BreakpointsViewModel _breakpointsViewModel;

    private readonly IPauseHandler _pauseHandler;

    public DebugWindowViewModel(IMessenger messenger, IUIDispatcher uiDispatcher,
        IPauseHandler pauseHandler, BreakpointsViewModel breakpointsViewModel,
        DisassemblyViewModel disassemblyViewModel, ModernDisassemblyViewModel modernDisassemblyViewModel, PaletteViewModel paletteViewModel,
        SoftwareMixerViewModel softwareMixerViewModel, VideoCardViewModel videoCardViewModel,
        CpuViewModel cpuViewModel, MidiViewModel midiViewModel, CfgCpuViewModel cfgCpuViewModel,
        MemoryViewModel memoryViewModel, StackMemoryViewModel stackMemoryViewModel
        ) {
        messenger.Register<AddViewModelMessage<DisassemblyViewModel>>(this);
        messenger.Register<AddViewModelMessage<MemoryViewModel>>(this);
        messenger.Register<RemoveViewModelMessage<DisassemblyViewModel>>(this);
        messenger.Register<RemoveViewModelMessage<MemoryViewModel>>(this);
        messenger.Register<AddViewModelMessage<ModernDisassemblyViewModel>>(this);
        messenger.Register<RemoveViewModelMessage<ModernDisassemblyViewModel>>(this);
        _messenger = messenger;
        _uiDispatcher = uiDispatcher;
        BreakpointsViewModel = breakpointsViewModel;
        StatusMessageViewModel = new(_uiDispatcher, _messenger);
        _pauseHandler = pauseHandler;
        IsPaused = pauseHandler.IsPaused;
        pauseHandler.Paused += () => uiDispatcher.Post(() => IsPaused = true);
        pauseHandler.Resumed += () => uiDispatcher.Post(() => IsPaused = false);
        DisassemblyViewModel disassemblyVm = disassemblyViewModel;
        DisassemblyViewModels.Add(disassemblyVm);
        ModernDisassemblyViewModels.Add(modernDisassemblyViewModel);
        PaletteViewModel = paletteViewModel;
        SoftwareMixerViewModel = softwareMixerViewModel;
        VideoCardViewModel = videoCardViewModel;
        CpuViewModel = cpuViewModel;
        MidiViewModel = midiViewModel;
        MemoryViewModels.Add(memoryViewModel);
        MemoryViewModels.Add(stackMemoryViewModel);
        CfgCpuViewModel = cfgCpuViewModel;
    }

    [RelayCommand]
    private void Pause() => _uiDispatcher.Post(() => {
        _pauseHandler.RequestPause("Pause button pressed in debug window");
    });

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void Continue() => _uiDispatcher.Post(_pauseHandler.Resume);

    public void Receive(AddViewModelMessage<DisassemblyViewModel> message) => DisassemblyViewModels.Add(message.ViewModel);
    public void Receive(AddViewModelMessage<MemoryViewModel> message) => MemoryViewModels.Add(message.ViewModel);
    public void Receive(RemoveViewModelMessage<DisassemblyViewModel> message) => DisassemblyViewModels.Remove(message.ViewModel);
    public void Receive(RemoveViewModelMessage<MemoryViewModel> message) => MemoryViewModels.Remove(message.ViewModel);
    public void Receive(AddViewModelMessage<ModernDisassemblyViewModel> message) => ModernDisassemblyViewModels.Add(message.ViewModel);
    public void Receive(RemoveViewModelMessage<ModernDisassemblyViewModel> message) => ModernDisassemblyViewModels.Remove(message.ViewModel);
}