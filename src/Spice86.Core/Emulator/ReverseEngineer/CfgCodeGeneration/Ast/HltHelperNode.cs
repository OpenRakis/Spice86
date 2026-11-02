namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>A generated <c>Hlt</c> helper invocation.</summary>
internal sealed record HltHelperNode : MethodCallNode {
    /// <summary>Creates the helper invocation.</summary>
    public HltHelperNode() : base(null, HelperCallNames.HltMethodName) {
    }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new HltHelperNode();
}
