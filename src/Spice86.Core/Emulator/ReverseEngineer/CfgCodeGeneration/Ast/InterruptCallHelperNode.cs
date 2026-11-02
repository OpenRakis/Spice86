namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>A generated <c>InterruptCall</c> helper invocation; the vector stays an argument.</summary>
internal sealed record InterruptCallHelperNode : MethodCallNode {
    /// <summary>Creates the helper invocation.</summary>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public InterruptCallHelperNode(params IVisitableAstNode[] arguments)
        : base(null, HelperCallNames.InterruptCallMethodName, arguments) {
    }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new InterruptCallHelperNode(arguments);
}
