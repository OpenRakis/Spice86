namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using System.Linq;

/// <summary>
/// A method's entry run of 16-bit register pushes printed as one <c>Stack.Save(...)</c> statement:
/// the helper performs the same stack writes, access checks and SP steps in the same order. Exact
/// because every value is a register or register-local read (no token read, no call) and not SP, so
/// no earlier push changes a value a later argument reads. Created by
/// <see cref="Ir.StackRuns"/> after the last body rewrite; the values sit in
/// <see cref="MethodCallNode.Arguments"/> so the generic walkers still see them.
/// </summary>
internal sealed record StackSaveNode : MethodCallNode {
    /// <summary>Creates the merged save of <paramref name="values"/> in push order.</summary>
    /// <param name="values">The pushed registers or register locals, in push order.</param>
    public StackSaveNode(IReadOnlyList<ValueNode> values)
        : base(HelperCallNames.StackPropertyPath, "Save", [.. values]) {
        Values = values;
    }

    /// <summary>The pushed registers or register locals, in push order.</summary>
    public IReadOnlyList<ValueNode> Values { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new StackSaveNode([.. arguments.Cast<ValueNode>()]);
}
