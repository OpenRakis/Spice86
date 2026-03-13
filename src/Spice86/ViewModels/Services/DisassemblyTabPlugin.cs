namespace Spice86.ViewModels.Services;

using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Parser;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;

internal sealed class DisassemblyTabPlugin : IDebuggerTabPlugin {
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly IDictionary<SegmentedAddress, FunctionInformation> _functionsInformation;
    private readonly BreakpointsViewModel _breakpointsViewModel;
    private readonly IPauseHandler _pauseHandler;
    private readonly IUIDispatcher _uiDispatcher;
    private readonly IMessenger _messenger;
    private readonly ITextClipboard _textClipboard;
    private readonly ILoggerService _loggerService;

    public DisassemblyTabPlugin(EmulatorBreakpointsManager emulatorBreakpointsManager, IMemory memory,
        State state, IDictionary<SegmentedAddress, FunctionInformation> functionsInformation,
        BreakpointsViewModel breakpointsViewModel, IPauseHandler pauseHandler,
        IUIDispatcher uiDispatcher, IMessenger messenger, ITextClipboard textClipboard,
        ILoggerService loggerService) {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _memory = memory;
        _state = state;
        _functionsInformation = functionsInformation;
        _breakpointsViewModel = breakpointsViewModel;
        _pauseHandler = pauseHandler;
        _uiDispatcher = uiDispatcher;
        _messenger = messenger;
        _textClipboard = textClipboard;
        _loggerService = loggerService;
    }

    public void Register(IDebuggerTabRegistry registry) {
        DisassemblyViewModel disassemblyViewModel = new(
            _emulatorBreakpointsManager, _memory, _state, _functionsInformation,
            _breakpointsViewModel, _pauseHandler, _uiDispatcher, _messenger,
            _textClipboard, _loggerService, canCloseTab: false);
        registry.Add(DebuggerTabIds.Disassembly, disassemblyViewModel);
    }
}
