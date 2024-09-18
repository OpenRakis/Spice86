namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

public class ExecutionContextManager : InstructionReplacer {
    private readonly ILoggerService _loggerService;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly Dictionary<SegmentedAddress, ISet<CfgInstruction>> _executionContextEntryPoints = new();
    private readonly CfgNodeFeeder _cfgNodeFeeder;
    private readonly IMemory _memory;
    private readonly State _state;
    private int _currentDepth;

    public ExecutionContextManager(IMemory memory, State state, EmulatorBreakpointsManager emulatorBreakpointsManager, CfgNodeFeeder cfgNodeFeeder,
        InstructionReplacerRegistry replacerRegistry, ILoggerService loggerService) : base(replacerRegistry) {
        _loggerService = loggerService;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _cfgNodeFeeder = cfgNodeFeeder;
        _memory = memory;
        _state = state;
        // Initial context at init
        CurrentExecutionContext = NewExecutionContext();
    }

    public ExecutionContext CurrentExecutionContext { get; private set; }

    private ExecutionContext NewExecutionContext() {
        return new(_currentDepth, new(_memory, _state, _loggerService));
    }
    public void SignalNewExecutionContext(SegmentedAddress entryAddress, SegmentedAddress? expectedReturnAddress) {
        // Save current execution context
        ExecutionContext previousExecutionContext = CurrentExecutionContext;
        // Create a new one at a higher depth
        _currentDepth++;
        CurrentExecutionContext = NewExecutionContext();
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
        CfgInstruction toExecute = _cfgNodeFeeder.CurrentNodeFromInstructionFeeder;
        if (!_executionContextEntryPoints.TryGetValue(entryAddress, out ISet<CfgInstruction>? nodes)) {
            nodes = new HashSet<CfgInstruction>();
            _executionContextEntryPoints.Add(entryAddress, nodes);
        }
        nodes.Add(toExecute);
    }

    public override void ReplaceInstruction(CfgInstruction old, CfgInstruction instruction) {
        if (_executionContextEntryPoints.TryGetValue(instruction.Address, out ISet<CfgInstruction>? entriesAtAddress) 
            && entriesAtAddress.Remove(old)) {
            entriesAtAddress.Add(instruction);
        }
    }
}