namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using System.Linq;

/// <summary>
/// An exit run of two to six register pops printed as one
/// <c>(T1, T2) = Stack.Restore2();</c> statement: the helper pops the same slots in the same order
/// and the assignments happen after the last pop. Exact because a pop reads only SS and SP, the
/// targets are distinct general registers other than SP or register locals, and no fault edge is
/// observed inside the run, so nothing observes the state between two pops. Created by
/// <see cref="Ir.StackRuns"/> after the last body rewrite; the targets sit in
/// <see cref="MethodCallNode.Arguments"/> so the generic walkers still see them.
/// </summary>
internal sealed record StackRestoreNode : MethodCallNode {
    /// <summary>Creates the merged restore assigning <paramref name="targets"/> in pop order.</summary>
    /// <param name="targets">The pop targets: distinct registers or register locals, in pop order.</param>
    public StackRestoreNode(IReadOnlyList<ValueNode> targets)
        : base(HelperCallNames.StackPropertyPath, $"Restore{targets.Count}", [.. targets]) {
        Targets = targets;
    }

    /// <summary>The pop targets: distinct registers or register locals, in pop order.</summary>
    public IReadOnlyList<ValueNode> Targets { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new StackRestoreNode([.. arguments.Cast<ValueNode>()]);
}
