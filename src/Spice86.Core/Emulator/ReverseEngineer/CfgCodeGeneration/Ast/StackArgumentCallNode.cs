namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using System.Linq;

/// <summary>
/// A typed call whose argument pushes (and dead-flag <c>add sp,n</c> clean-up) print folded into a
/// single <c>CallWithStackArguments</c> statement: the helper performs the pushes, the typed call
/// and the SP step with the same memory writes and faults as the original instructions. Created by
/// <see cref="Ir.StackArguments"/> when the caller's push run matched the callee's parameter slots.
/// The stack argument expressions sit at the tail of <see cref="MethodCallNode.Arguments"/> so the
/// generic rewrites (register-local promotion, effects walks) still see them.
/// </summary>
internal sealed record StackArgumentCallNode : MethodCallNode {
    /// <summary>Creates the folded call.</summary>
    /// <param name="call">The typed call the merge replaces; its arguments carry the return CS/IP.</param>
    /// <param name="stackArguments">The argument expressions in callee slot order.</param>
    /// <param name="cleanupBytes">The bytes of the caller clean-up folded in, 0 for none.</param>
    public StackArgumentCallNode(ProcedureCallHelperNode call,
        IReadOnlyList<IVisitableAstNode> stackArguments, int cleanupBytes)
        : base(null, "CallWithStackArguments", [.. call.Arguments, .. stackArguments]) {
        Call = call;
        CleanupBytes = cleanupBytes;
    }

    /// <summary>The typed call the merge replaces.</summary>
    public ProcedureCallHelperNode Call { get; }

    /// <summary>The argument expressions in callee slot order, printed after the lambda.</summary>
    public IReadOnlyList<IVisitableAstNode> StackArguments =>
        [.. Arguments.Skip(Call.Arguments.Count)];

    /// <summary>The bytes of the caller clean-up the helper performs, 0 for none.</summary>
    public int CleanupBytes { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new StackArgumentCallNode(
            (ProcedureCallHelperNode)Call.WithArguments(arguments[..Call.Arguments.Count]),
            arguments[Call.Arguments.Count..], CleanupBytes);
}
