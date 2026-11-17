namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// Reads or writes the CPU halted state (<c>State.IsHalted</c>): the generated HLT wait loop sets it and
/// loops on it; the external event check that delivers the resuming interrupt clears it.
/// </summary>
internal sealed record HaltedValueNode : MethodCallValueNode {
    /// <summary>Creates a halted-state reference.</summary>
    public HaltedValueNode() : base(DataType.BOOL, nameof(State), nameof(State.IsHalted)) {
    }

    /// <inheritdoc />
    public override MethodCallValueNode WithArguments(IVisitableAstNode[] arguments) => new HaltedValueNode();
}
