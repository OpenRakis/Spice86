namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A run of pops whose register writes the dead-write removal proved nobody reads, printed as one
/// <c>Stack.Release(n)</c> statement: the helper performs the same stack reads, access checks and SP
/// steps in the same order and discards the values. Created by <see cref="Ir.StackRuns"/> after the
/// last body rewrite.
/// </summary>
internal sealed record StackReleaseNode : MethodCallNode {
    /// <summary>Creates the merged release of <paramref name="count"/> discarded pops.</summary>
    /// <param name="count">The number of pops to discard.</param>
    public StackReleaseNode(int count)
        : base(HelperCallNames.StackPropertyPath, "Release", new ConstantNode(DataType.UINT32, (ulong)count)) {
        Count = count;
    }

    /// <summary>The number of pops to discard.</summary>
    public int Count { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new StackReleaseNode(arguments[0] is ConstantNode constant ? (int)constant.Value : 0);
}
