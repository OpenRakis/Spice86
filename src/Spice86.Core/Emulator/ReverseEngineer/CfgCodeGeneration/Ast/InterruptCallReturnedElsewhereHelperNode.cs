namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>A generated <c>InterruptCallReturnedElsewhere</c> helper invocation; the result tells
/// whether the interrupt call ended at one of the redirected return addresses passed as its last
/// argument.</summary>
internal sealed record InterruptCallReturnedElsewhereHelperNode : MethodCallValueNode {
    /// <summary>Creates the helper invocation.</summary>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public InterruptCallReturnedElsewhereHelperNode(params IVisitableAstNode[] arguments)
        : base(DataType.BOOL,
            new MethodCallNode(null, HelperCallNames.InterruptCallReturnedElsewhereMethodName, arguments)) {
    }

    /// <inheritdoc />
    public override MethodCallValueNode WithArguments(IVisitableAstNode[] arguments) =>
        new InterruptCallReturnedElsewhereHelperNode(arguments);
}
