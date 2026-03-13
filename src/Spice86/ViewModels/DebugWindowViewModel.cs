namespace Spice86.ViewModels;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.VM;
using Spice86.ViewModels.Messages;
using Spice86.ViewModels.Services;

public partial class DebugWindowViewModel : ViewModelBase,
    IRecipient<AddViewModelMessage<DisassemblyViewModel>>, IRecipient<AddViewModelMessage<MemoryViewModel>>,
    IRecipient<RemoveViewModelMessage<DisassemblyViewModel>>, IRecipient<RemoveViewModelMessage<MemoryViewModel>> {

    private readonly IMessenger _messenger;
    private readonly IUIDispatcher _uiDispatcher;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private AvaloniaList<DebuggerSubTabViewModel> _deviceSubTabs = new();

    [ObservableProperty]
    private DebuggerSubTabViewModel? _selectedDeviceSubTab;

    [ObservableProperty]
    private AvaloniaList<IDebuggerTabContentViewModel> _memoryViews = new();

    [ObservableProperty]
    private IDebuggerTabContentViewModel? _selectedMemoryView;

    [ObservableProperty]
    private CpuViewModel _cpuViewModel;

    [ObservableProperty]
    private AvaloniaList<DisassemblyViewModel> _disassemblyViewModels = new();

    [ObservableProperty]
    private CfgCpuViewModel _cfgCpuViewModel;

    [ObservableProperty]
    private StatusMessageViewModel _statusMessageViewModel;

    [ObservableProperty]
    private BreakpointsViewModel _breakpointsViewModel;

    private readonly IPauseHandler _pauseHandler;

    public DebugWindowViewModel(IMessenger messenger, IUIDispatcher uiDispatcher,
        IPauseHandler pauseHandler, IDebuggerTabRegistry tabRegistry) {
        messenger.Register<AddViewModelMessage<DisassemblyViewModel>>(this);
        messenger.Register<AddViewModelMessage<MemoryViewModel>>(this);
        messenger.Register<RemoveViewModelMessage<DisassemblyViewModel>>(this);
        messenger.Register<RemoveViewModelMessage<MemoryViewModel>>(this);
        _messenger = messenger;
        _uiDispatcher = uiDispatcher;
        BreakpointsViewModel = tabRegistry.Get<BreakpointsViewModel>(DebuggerTabIds.Breakpoints);
        StatusMessageViewModel = new(_uiDispatcher, _messenger);
        _pauseHandler = pauseHandler;
        IsPaused = pauseHandler.IsPaused;
        pauseHandler.Paused += () => uiDispatcher.Post(() => IsPaused = true);
        pauseHandler.Resumed += () => uiDispatcher.Post(() => IsPaused = false);
        DisassemblyViewModel disassemblyVm = tabRegistry.Get<DisassemblyViewModel>(DebuggerTabIds.Disassembly);
        DisassemblyViewModels.Add(disassemblyVm);
        CpuViewModel = tabRegistry.Get<CpuViewModel>(DebuggerTabIds.Cpu);
        MemoryViews.AddRange(tabRegistry.Get<IReadOnlyList<IDebuggerTabContentViewModel>>(DebuggerTabIds.MemoryViews));
        SelectedMemoryView = MemoryViews.FirstOrDefault();
        CfgCpuViewModel = tabRegistry.Get<CfgCpuViewModel>(DebuggerTabIds.CfgCpu);
        DeviceSubTabs.AddRange(tabRegistry.GetSubTabs(DebuggerTabIds.DevicesGroup));
        SelectedDeviceSubTab = DeviceSubTabs.FirstOrDefault();
    }

    [RelayCommand]
    private void Pause() => _uiDispatcher.Post(() => {
        _pauseHandler.RequestPause("Pause button pressed in debug window");
    });

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void Continue() => _uiDispatcher.Post(_pauseHandler.Resume);

    public void Receive(AddViewModelMessage<DisassemblyViewModel> message) => DisassemblyViewModels.Add(message.ViewModel);
    public void Receive(AddViewModelMessage<MemoryViewModel> message) {
        MemoryViews.Add(message.ViewModel);
        SelectedMemoryView = message.ViewModel;
    }
    public void Receive(RemoveViewModelMessage<DisassemblyViewModel> message) => DisassemblyViewModels.Remove(message.ViewModel);
    public void Receive(RemoveViewModelMessage<MemoryViewModel> message) {
        MemoryViews.Remove(message.ViewModel);
        if (ReferenceEquals(SelectedMemoryView, message.ViewModel)) {
            SelectedMemoryView = MemoryViews.FirstOrDefault();
        }
    }
}