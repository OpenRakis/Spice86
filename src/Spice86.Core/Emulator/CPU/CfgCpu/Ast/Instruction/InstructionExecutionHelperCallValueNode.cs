namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>A value returned by a typed protected-mode execution helper.</summary>
public sealed record InstructionExecutionHelperCallValueNode : MethodCallValueNode {
    /// <summary>Creates a value-returning helper call.</summary>
    /// <param name="dataType">The helper's return type.</param>
    /// <param name="operation">The helper operation.</param>
    /// <param name="arguments">The helper arguments.</param>
    public InstructionExecutionHelperCallValueNode(DataType dataType,
        InstructionExecutionHelperOperation operation, params IVisitableAstNode[] arguments)
        : base(dataType, new InstructionExecutionHelperCallNode(operation, arguments)) {
        Operation = operation;
    }

    /// <summary>The helper operation.</summary>
    public InstructionExecutionHelperOperation Operation { get; }

    /// <inheritdoc />
    public override MethodCallValueNode WithArguments(IVisitableAstNode[] arguments) =>
        new InstructionExecutionHelperCallValueNode(DataType, Operation, arguments);
}
