namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>Reads the current 8087 control word without changing CPU flags.</summary>
public sealed record FpuControlWordNode : MethodCallValueNode {
    /// <summary>Creates a control-word read.</summary>
    public FpuControlWordNode() : base(DataType.UINT16, nameof(State), nameof(State.FpuControlWord)) {
    }

    /// <inheritdoc />
    public override MethodCallValueNode WithArguments(IVisitableAstNode[] arguments) => new FpuControlWordNode();
}
