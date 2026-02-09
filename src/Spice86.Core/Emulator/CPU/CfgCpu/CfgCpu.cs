namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.Logging;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

public class CfgCpu : IFunctionHandlerProvider {
    private readonly ILoggerService _loggerService;
    private readonly InstructionExecutionHelper _instructionExecutionHelper;
    private readonly State _state;
    private readonly DualPic _dualPic;
    private readonly ExecutionContextManager _executionContextManager;
    private readonly InstructionReplacerRegistry _replacerRegistry = new();
    private readonly CpuHeavyLogger? _cpuHeavyLogger;

    public CfgCpu(IMemory memory, State state, IOPortDispatcher ioPortDispatcher, CallbackHandler callbackHandler,
        DualPic dualPic, EmulatorBreakpointsManager emulatorBreakpointsManager,
        FunctionCatalogue functionCatalogue,
        bool useCodeOverride, bool failOnInvalidOpcode, ILoggerService loggerService, CpuHeavyLogger? cpuHeavyLogger = null) {
        _loggerService = loggerService;
        _state = state;
        _dualPic = dualPic;
        _cpuHeavyLogger = cpuHeavyLogger;

        CfgNodeFeeder = new(memory, state, emulatorBreakpointsManager, _replacerRegistry);
        _executionContextManager = new(memory, state, CfgNodeFeeder, _replacerRegistry, functionCatalogue, useCodeOverride, loggerService);
        _instructionExecutionHelper = new(state, memory, ioPortDispatcher, callbackHandler, emulatorBreakpointsManager, _executionContextManager, failOnInvalidOpcode, loggerService);
    }
    
    /// <summary>
    /// Handles at high level parsing, linking and book keeping of nodes from the graph.
    /// </summary>
    public CfgNodeFeeder CfgNodeFeeder { get; }

    /// <summary>
    /// Handles the various contexts of the execution.
    /// A context contains a graph where links between instructions are due to regular execution flow internal to the CPU.
    /// Switching context is caused by external interrupts.
    /// </summary>
    public ExecutionContextManager ExecutionContextManager => _executionContextManager;

    public FunctionHandler FunctionHandlerInUse => ExecutionContextManager.CurrentExecutionContext.FunctionHandler;
    public bool IsInitialExecutionContext => ExecutionContextManager.CurrentExecutionContext.Depth == 0;
    private ExecutionContext CurrentExecutionContext => _executionContextManager.CurrentExecutionContext;
    
    /// <inheritdoc />
    public void ExecuteNext() {
        ICfgNode toExecute = CfgNodeFeeder.GetLinkedCfgNodeToExecute(CurrentExecutionContext);

        // Execute the node
        try {
            _loggerService.LoggerPropertyBag.CsIp = toExecute.Address;
            
            // Direct execution (temporary until all instructions have GenerateExecutionAst implemented):
            toExecute.Execute(_instructionExecutionHelper);

            // TODO: Enable AST execution once all instructions implement GenerateExecutionAst
            //
            // Current status:
            // - OpRegRm instructions (ADD, SUB, AND, OR, XOR, CMP, ADC, SBB) are implemented with IP advancement
            // - IP advancement is included in the AST via MoveIpNextNode wrapped in BlockNode
            // - Remaining instructions will throw NotImplementedException
            //
            // To enable:
            // 1. Uncomment the block below
            // 2. Comment out the direct Execute() call above
            // 3. Run MachineTest to identify missing implementations
            // 4. Implement GenerateExecutionAst() for failing instructions following the OpRegRm pattern
            // 5. Iterate until all tests pass
            //
            // See code-review-findings.md Issue #2 for MoveIpAndSetNextNode handling
            // See code-review-findings-issue-2-plan.md for implementation plan
            // See microcode-ast.md for full plan and remaining work
            //
            // IVisitableAstNode executionAst = toExecute.GenerateExecutionAst(new Ast.Builder.AstBuilder());
            // InstructionExecutor.Expressions.AstExpressionBuilder expressionBuilder = new();
            // System.Linq.Expressions.Expression astExpression = executionAst.Accept(expressionBuilder);
            // Action<InstructionExecutionHelper, State, Memory> compiledAction =
            //     expressionBuilder.ToActionWithHelper(astExpression).Compile();
            // compiledAction(_instructionExecutionHelper, _state, (_instructionExecutionHelper.Memory as Memory)!);
            
        } catch (CpuException e) {
            if(toExecute is CfgInstruction cfgInstruction) {
                _instructionExecutionHelper.HandleCpuException(cfgInstruction, e);
            }
        }

        // Log instruction after execution if CPU heavy logging is enabled
        _cpuHeavyLogger?.LogInstruction(toExecute);

        ICfgNode? nextToExecute = toExecute.GetNextSuccessor(_instructionExecutionHelper);
        
        _state.IncCycles();

        // Register what was executed and what is next node according to the graph in the execution context for next pass
        CurrentExecutionContext.LastExecuted = toExecute;
        CurrentExecutionContext.NodeToExecuteNextAccordingToGraph = nextToExecute;
        HandleExternalInterrupt(toExecute);
    }

    /// <summary>
    /// <inheritdoc />
    /// </summary>
    public void SignalEntry() {
        _executionContextManager.SignalEntry();
        // Parse the first instruction and register it as entry point
        CfgNodeFeeder.GetLinkedCfgNodeToExecute(CurrentExecutionContext);
        _executionContextManager.CurrentExecutionContext.FunctionHandler.Call(CallType.MACHINE, _state.IpSegmentedAddress, null, null);
        // expected return address from machine start is never defined.
        _executionContextManager.SignalNewExecutionContext(_state.IpSegmentedAddress, SegmentedAddress.ZERO);
    }

    public void SignalEnd() {
        _executionContextManager.CurrentExecutionContext.FunctionHandler.Ret(CallType.MACHINE, null);
    }

    /// <summary>
    /// Handles external interrupts that may need to be processed after the execution of a control flow graph (CFG) node.
    /// </summary>
    /// <param name="toExecute">The CFG node currently being executed, which may influence interrupt and context handling.</param>
    private void HandleExternalInterrupt(ICfgNode toExecute) {
        // Before any external interrupt has a chance to execute, check if we landed in a place where context should be switched.
        if (toExecute.CanCauseContextRestore) {
            // We only attempt to restore contexts after IRET
            // Otherwise, we may hit via regular flow an instruction that is at the return address of an existing IRET and that is waiting to be restored, and restore it. 
            _executionContextManager.RestoreExecutionContextIfNeeded(_state.IpSegmentedAddress);
        }

        if (_state.InterruptShadowing) {
            // Interrupts are inhibited for this instruction only.
            // Is set either via POP SS, MOV SS/sreg or STI.
            _state.InterruptShadowing = false;
            return;
        }

        if (!_state.InterruptFlag) {
            return;
        }

        byte? externalInterruptVectorNumber = _dualPic.ComputeVectorNumber();
        if (externalInterruptVectorNumber == null) {
            return;
        }
        (SegmentedAddress target, SegmentedAddress expectedReturn) = _instructionExecutionHelper.DoInterrupt(externalInterruptVectorNumber.Value);

        _executionContextManager.SignalNewExecutionContext(target, expectedReturn);
        _executionContextManager.CurrentExecutionContext.FunctionHandler.Call(CallType.EXTERNAL_INTERRUPT, _state.IpSegmentedAddress, expectedReturn, null);
    }
}