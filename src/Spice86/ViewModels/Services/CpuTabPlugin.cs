namespace Spice86.ViewModels.Services;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.ViewModels;

internal sealed class CpuTabPlugin : IDebuggerTabPlugin {
    private readonly State _state;
    private readonly IMemory _memory;
    private readonly IPauseHandler _pauseHandler;
    private readonly IUIDispatcher _uiDispatcher;

    public CpuTabPlugin(State state, IMemory memory, IPauseHandler pauseHandler, IUIDispatcher uiDispatcher) {
        _state = state;
        _memory = memory;
        _pauseHandler = pauseHandler;
        _uiDispatcher = uiDispatcher;
    }

    public void Register(IDebuggerTabRegistry registry) {
        registry.Add(DebuggerTabId.Cpu, new CpuViewModel(_state, _memory, _pauseHandler, _uiDispatcher));
    }
}
