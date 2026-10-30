namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A void <c>CheckBound</c> helper call (the BOUND bounds check). Carries the operand width as a
/// field; the method name is computed by <see cref="HelperCallNames"/>.
/// </summary>
public sealed record BoundCheckCallNode : MethodCallNode {
    /// <summary>
    /// Initializes a new instance of the BoundCheckCallNode class.
    /// </summary>
    /// <param name="width">The operand width of the check.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public BoundCheckCallNode(BitWidth width, params IVisitableAstNode[] arguments)
        : base(null, HelperCallNames.BoundCheckMethodName, arguments) {
        Width = width;
    }

    /// <summary>The operand width of the check.</summary>
    public BitWidth Width { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new BoundCheckCallNode(Width, arguments);
}
