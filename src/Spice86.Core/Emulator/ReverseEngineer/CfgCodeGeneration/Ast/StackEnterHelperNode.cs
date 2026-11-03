namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>Creates a stack frame using the runtime stack-address width and the supplied operand size.</summary>
internal sealed record StackEnterHelperNode : MethodCallNode {
    /// <summary>Creates the frame helper call.</summary>
    /// <param name="arguments">Storage size, nesting level and whether the operand size is 32 bits.</param>
    public StackEnterHelperNode(params IVisitableAstNode[] arguments)
        : base(nameof(Stack), nameof(Stack.Enter), arguments) {
    }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new StackEnterHelperNode(arguments);
}
