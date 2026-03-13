namespace Spice86.ViewModels.Services;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.VM;
using Spice86.ViewModels;

internal sealed class CfgCpuTabPlugin : IDebuggerTabPlugin {
    private readonly IUIDispatcher _uiDispatcher;
    private readonly ExecutionContextManager _executionContextManager;
    private readonly IPauseHandler _pauseHandler;
    private readonly NodeToString _nodeToString;

    public CfgCpuTabPlugin(IUIDispatcher uiDispatcher, ExecutionContextManager executionContextManager,
        IPauseHandler pauseHandler, NodeToString nodeToString) {
        _uiDispatcher = uiDispatcher;
        _executionContextManager = executionContextManager;
        _pauseHandler = pauseHandler;
        _nodeToString = nodeToString;
    }

    public void Register(IDebuggerTabRegistry registry) {
        registry.Add(DebuggerTabIds.CfgCpu,
            new CfgCpuViewModel(_uiDispatcher, _executionContextManager, _pauseHandler, _nodeToString));
    }
}
