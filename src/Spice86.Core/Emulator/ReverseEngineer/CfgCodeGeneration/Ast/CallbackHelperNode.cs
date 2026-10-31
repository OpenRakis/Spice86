namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>A generated <c>Callback</c> helper invocation.</summary>
internal sealed record CallbackHelperNode : MethodCallNode {
    /// <summary>Creates the helper invocation.</summary>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public CallbackHelperNode(params IVisitableAstNode[] arguments)
        : base(null, HelperCallNames.CallbackMethodName, arguments) {
    }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new CallbackHelperNode(arguments);
}
