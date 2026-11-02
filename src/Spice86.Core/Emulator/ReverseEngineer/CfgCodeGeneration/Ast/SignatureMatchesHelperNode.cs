namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>A generated <c>SelectorSignatureMatches</c> helper invocation; the result tells whether
/// the bytes at the selector address still match the guarded signature.</summary>
internal sealed record SignatureMatchesHelperNode : MethodCallValueNode {
    /// <summary>Creates the helper invocation.</summary>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public SignatureMatchesHelperNode(params IVisitableAstNode[] arguments)
        : base(DataType.BOOL,
            new MethodCallNode(null, HelperCallNames.SignatureMatchesMethodName, arguments)) {
    }

    /// <inheritdoc />
    public override MethodCallValueNode WithArguments(IVisitableAstNode[] arguments) =>
        new SignatureMatchesHelperNode(arguments);
}
