namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

public class ExecutionContextManager : InstructionReplacer {
    private readonly ILoggerService _loggerService;
    private readonly Dictionary<SegmentedAddress, ISet<CfgInstruction>> _executionContextEntryPoints = new();
    private readonly CfgNodeFeeder _cfgNodeFeeder;
    private readonly IMemory _memory;
    private readonly State _state;
    /// <summary>
    /// Maps return address to an execution context to restore. Cant be done with breakpoints since sometimes external int will happen even before instruction after ret / iret is executed
    /// </summary>
    private readonly Dictionary<SegmentedAddress, ExecutionContext> _executionContextReturns = new();
    private int _currentDepth;
    private readonly FunctionCatalogue _functionCatalogue;

    public ExecutionContextManager(IMemory memory, State state, CfgNodeFeeder cfgNodeFeeder,
        InstructionReplacerRegistry replacerRegistry, FunctionCatalogue functionCatalogue, ILoggerService loggerService) : base(replacerRegistry) {
        _loggerService = loggerService;
        _cfgNodeFeeder = cfgNodeFeeder;
        _memory = memory;
        _state = state;
        _functionCatalogue = functionCatalogue;
        // Initial context at init
        CurrentExecutionContext = NewExecutionContext();
    }

    public ExecutionContext CurrentExecutionContext { get; private set; }

    private ExecutionContext NewExecutionContext() {
        return new(_currentDepth, new(_memory, _state, null, _functionCatalogue, _loggerService));
    }

    public void SignalNewExecutionContext(SegmentedAddress entryAddress, SegmentedAddress? expectedReturnAddress) {
        // Save current execution context
        if (expectedReturnAddress != null) {
            _executionContextReturns.Add(expectedReturnAddress.Value, CurrentExecutionContext);
        }
        // Create a new one at a higher depth
        _currentDepth++;
        CurrentExecutionContext = NewExecutionContext();
        RegisterCurrentInstructionAsEntryPoint(entryAddress);
    }

    public void RestoreExecutionContextIfNeeded(SegmentedAddress returnAddress) {
        if (_executionContextReturns.Remove(returnAddress, out ExecutionContext? previousExecutionContext)) {
            RestoreExecutionContext(previousExecutionContext);
        }
    }

    private void RestoreExecutionContext(ExecutionContext previousExecutionContext) {
        // Restore previous execution context and depth
        CurrentExecutionContext = previousExecutionContext;
        _currentDepth--;
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