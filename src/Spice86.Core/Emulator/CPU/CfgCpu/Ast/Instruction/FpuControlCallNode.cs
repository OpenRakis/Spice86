namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>An FPU reset or interrupt-mask operation on the limited 8087 state.</summary>
public sealed record FpuControlCallNode : MethodCallNode {
    /// <summary>Creates a typed FPU control operation.</summary>
    /// <param name="operation">The control instruction.</param>
    public FpuControlCallNode(InstructionOperation operation)
        : base(nameof(State), operation switch {
            InstructionOperation.FNINIT => nameof(State.InitializeFpu),
            InstructionOperation.FNDISI => nameof(State.DisableFpuInterrupts),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        }) {
        Operation = operation;
    }

    /// <summary>The control instruction.</summary>
    public InstructionOperation Operation { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new FpuControlCallNode(Operation);
}
