namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>A control-word load, initialization or interrupt-mask operation on the limited 8087 state.</summary>
public sealed record FpuControlCallNode : MethodCallNode {
    /// <summary>Creates a typed FPU control operation.</summary>
    /// <param name="operation">The control instruction.</param>
    /// <param name="arguments">The control word for FLDCW; empty for the other instructions.</param>
    public FpuControlCallNode(InstructionOperation operation, params IVisitableAstNode[] arguments)
        : base(nameof(State), operation switch {
            InstructionOperation.FLDCW => nameof(State.SetFpuControlWord),
            InstructionOperation.FNINIT => nameof(State.InitializeFpu),
            InstructionOperation.FNDISI => nameof(State.DisableFpuInterrupts),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        }, arguments) {
        Operation = operation;
    }

    /// <summary>The control instruction.</summary>
    public InstructionOperation Operation { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new FpuControlCallNode(Operation, arguments);
}
