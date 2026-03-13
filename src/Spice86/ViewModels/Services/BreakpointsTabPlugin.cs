namespace Spice86.ViewModels.Services;

using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.ViewModels;

public sealed class BreakpointsTabPlugin : IDebuggerTabPlugin {
    private readonly State _state;
    private readonly IPauseHandler _pauseHandler;
    private readonly IMessenger _messenger;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IUIDispatcher _uiDispatcher;
    private readonly ITextClipboard _textClipboard;
    private readonly IMemory _memory;

    public BreakpointsTabPlugin(State state, IPauseHandler pauseHandler, IMessenger messenger,
        EmulatorBreakpointsManager emulatorBreakpointsManager, IUIDispatcher uiDispatcher,
        ITextClipboard textClipboard, IMemory memory) {
        _state = state;
        _pauseHandler = pauseHandler;
        _messenger = messenger;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _uiDispatcher = uiDispatcher;
        _textClipboard = textClipboard;
        _memory = memory;
    }

    public void Register(IDebuggerTabRegistry registry) {
        registry.Add(DebuggerTabIds.Breakpoints,
            CreateViewModel(_state, _pauseHandler, _messenger, _emulatorBreakpointsManager, _uiDispatcher, _textClipboard, _memory));
    }

    public static BreakpointsViewModel CreateViewModel(State state, IPauseHandler pauseHandler, IMessenger messenger,
        EmulatorBreakpointsManager emulatorBreakpointsManager, IUIDispatcher uiDispatcher,
        ITextClipboard textClipboard, IMemory memory) {
        return new BreakpointsViewModel(state, pauseHandler, messenger, emulatorBreakpointsManager, uiDispatcher, textClipboard, memory);
    }
}
