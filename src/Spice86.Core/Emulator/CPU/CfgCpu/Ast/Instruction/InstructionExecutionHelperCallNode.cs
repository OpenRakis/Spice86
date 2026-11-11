namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>A typed protected-mode execution helper call.</summary>
public sealed record InstructionExecutionHelperCallNode : MethodCallNode {
    /// <summary>Creates a helper call with its operation and arguments.</summary>
    /// <param name="operation">The helper operation.</param>
    /// <param name="arguments">The helper arguments.</param>
    public InstructionExecutionHelperCallNode(InstructionExecutionHelperOperation operation,
        params IVisitableAstNode[] arguments)
        : base(operation == InstructionExecutionHelperOperation.PopSegmentSelector ? "Stack" : null,
            operation.ToString(), arguments) {
        Operation = operation;
    }

    /// <summary>The helper operation.</summary>
    public InstructionExecutionHelperOperation Operation { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new InstructionExecutionHelperCallNode(Operation, arguments);
}
