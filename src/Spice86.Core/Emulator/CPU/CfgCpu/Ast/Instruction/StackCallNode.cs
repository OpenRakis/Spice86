namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A void <c>Stack</c> helper call (Push, Leave, PushAll, PopAll). Carries the operation and width as
/// fields; the method name is computed by <see cref="HelperCallNames"/>.
/// </summary>
public sealed record StackCallNode : MethodCallNode {
    /// <summary>
    /// Initializes a new instance of the StackCallNode class.
    /// </summary>
    /// <param name="operation">The stack operation of the call.</param>
    /// <param name="width">The stack width of the call.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public StackCallNode(StackOperation operation, BitWidth width, params IVisitableAstNode[] arguments)
        : base(HelperCallNames.StackPropertyPath, HelperCallNames.StackMethodName(operation, width), arguments) {
        Operation = operation;
        Width = width;
    }

    /// <summary>The stack operation of the call.</summary>
    public StackOperation Operation { get; }

    /// <summary>The stack width of the call.</summary>
    public BitWidth Width { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new StackCallNode(Operation, Width, arguments);
}
