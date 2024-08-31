namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Infrastructure;
using Spice86.Messages;
using Spice86.Shared.Diagnostics;

public partial class DebugWindowViewModel : ViewModelBase,
    IRecipient<AddViewModelMessage<DisassemblyViewModel>>, IRecipient<AddViewModelMessage<MemoryViewModel>>,
    IRecipient<RemoveViewModelMessage<DisassemblyViewModel>>, IRecipient<RemoveViewModelMessage<MemoryViewModel>> {

    private readonly IMessenger _messenger;

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

    public DebugWindowViewModel(State cpuState, IMemory memory, Midi externalMidiDevice,
        ArgbPalette argbPalette, SoftwareMixer softwareMixer, IVgaRenderer vgaRenderer, VideoState videoState,
        ExecutionContextManager executionContextManager, IMessenger messenger, IUIDispatcher uiDispatcher,
        ITextClipboard textClipboard, IHostStorageProvider storageProvider, EmulatorBreakpointsManager emulatorBreakpointsManager,
        IStructureViewModelFactory structureViewModelFactory, IPauseHandler pauseHandler) {
        messenger.Register<AddViewModelMessage<DisassemblyViewModel>>(this);
        messenger.Register<AddViewModelMessage<MemoryViewModel>>(this);
        messenger.Register<RemoveViewModelMessage<DisassemblyViewModel>>(this);
        messenger.Register<RemoveViewModelMessage<MemoryViewModel>>(this);
        _messenger = messenger;
        _pauseHandler = pauseHandler;
        IsPaused = pauseHandler.IsPaused;
        pauseHandler.Pausing += () => IsPaused = true;
        pauseHandler.Resumed += () => IsPaused = false;
        DisassemblyViewModel disassemblyVm = new(memory, cpuState, pauseHandler, uiDispatcher, messenger, textClipboard, emulatorBreakpointsManager);
        DisassemblyViewModels.Add(disassemblyVm);
        PaletteViewModel = new(argbPalette);
        SoftwareMixerViewModel = new(softwareMixer);
        VideoCardViewModel = new(vgaRenderer, videoState);
        CpuViewModel = new(cpuState, pauseHandler);
        MidiViewModel = new(externalMidiDevice);
        MemoryViewModels.Add(new(memory, pauseHandler, messenger, uiDispatcher, textClipboard, storageProvider, structureViewModelFactory));
        CfgCpuViewModel = new(executionContextManager, pauseHandler, new PerformanceMeasurer());
    }

    [RelayCommand]
    private void Pause() {
        _pauseHandler.RequestPause("Pause button pressed in debug window");
        _messenger.Send(new UpdateViewMessage());
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void Continue() => _pauseHandler.Resume();

    public void Receive(AddViewModelMessage<DisassemblyViewModel> message) => DisassemblyViewModels.Add(message.ViewModel);
    public void Receive(AddViewModelMessage<MemoryViewModel> message) => MemoryViewModels.Add(message.ViewModel);
    public void Receive(RemoveViewModelMessage<DisassemblyViewModel> message) => DisassemblyViewModels.Remove(message.ViewModel);
    public void Receive(RemoveViewModelMessage<MemoryViewModel> message) => MemoryViewModels.Remove(message.ViewModel);
}