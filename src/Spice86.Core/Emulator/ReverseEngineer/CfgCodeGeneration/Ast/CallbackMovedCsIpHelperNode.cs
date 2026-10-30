namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>A generated <c>CallbackMovedCsIp</c> helper invocation; the result tells whether the
/// callback moved CS:IP.</summary>
internal sealed record CallbackMovedCsIpHelperNode : MethodCallValueNode {
    /// <summary>Creates the helper invocation.</summary>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public CallbackMovedCsIpHelperNode(params IVisitableAstNode[] arguments)
        : base(DataType.BOOL,
            new MethodCallNode(null, HelperCallNames.CallbackMovedCsIpMethodName, arguments)) {
    }

    /// <inheritdoc />
    public override MethodCallValueNode WithArguments(IVisitableAstNode[] arguments) =>
        new CallbackMovedCsIpHelperNode(arguments);
}
