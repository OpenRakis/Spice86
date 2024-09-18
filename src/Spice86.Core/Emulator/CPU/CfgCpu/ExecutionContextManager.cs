namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

public class ExecutionContextManager : InstructionReplacer {
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly Dictionary<SegmentedAddress, ISet<ICfgNode>> _executionContextEntryPoints = new();
    private readonly CfgNodeFeeder _cfgNodeFeeder;
    private int _currentDepth;

    public ExecutionContextManager(EmulatorBreakpointsManager emulatorBreakpointsManager, CfgNodeFeeder cfgNodeFeeder,
        InstructionReplacerRegistry replacerRegistry) : base(replacerRegistry) {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _cfgNodeFeeder = cfgNodeFeeder;
        // Initial context at init
        CurrentExecutionContext = new(_currentDepth);
    }

    public ExecutionContext CurrentExecutionContext { get; private set; }

    public void SignalNewExecutionContext(SegmentedAddress entryAddress, SegmentedAddress? expectedReturnAddress) {
        // Save current execution context
        ExecutionContext previousExecutionContext = CurrentExecutionContext;
        // Create a new one at a higher depth
        _currentDepth++;
        CurrentExecutionContext = new(_currentDepth);
        if (expectedReturnAddress != null) {
            // breakpoint that deletes itself on reach. Should be triggered when the return address is reached and before it starts execution.
            _emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.EXECUTION, expectedReturnAddress.Value.ToPhysical(), (_) => {
                // Restore previous execution context and depth
                CurrentExecutionContext = previousExecutionContext;
                _currentDepth--;
            }, true), true);
        }

        RegisterCurrentInstructionAsEntryPoint(entryAddress);
    }

    private void RegisterCurrentInstructionAsEntryPoint(SegmentedAddress entryAddress) {
        // Register a new entry point
        ICfgNode toExecute = _cfgNodeFeeder.GetLinkedCfgNodeToExecute(CurrentExecutionContext);
        if (!_executionContextEntryPoints.TryGetValue(entryAddress, out ISet<ICfgNode>? nodes)) {
            nodes = new HashSet<ICfgNode>();
            _executionContextEntryPoints.Add(entryAddress, nodes);
        }
        nodes.Add(toExecute);
    }

    public override void ReplaceInstruction(CfgInstruction old, CfgInstruction instruction) {
        if (_executionContextEntryPoints.TryGetValue(instruction.Address, out ISet<ICfgNode>? entriesAtAddress) 
            && entriesAtAddress.Remove(old)) {
            entriesAtAddress.Add(instruction);
        }
    }
}