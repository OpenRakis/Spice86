namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A value-returning <c>Stack</c> helper call (Pop, Peek). Carries the operation and width as fields;
/// the method name is computed by <see cref="HelperCallNames"/>.
/// </summary>
public sealed record StackCallValueNode : MethodCallValueNode {
    /// <summary>
    /// Initializes a new instance of the StackCallValueNode class.
    /// </summary>
    /// <param name="operation">The stack operation of the call.</param>
    /// <param name="width">The stack width of the call; the returned value is its unsigned type.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public StackCallValueNode(StackOperation operation, BitWidth width, params IVisitableAstNode[] arguments)
        : base(DataType.UnsignedFromBitWidth(width), new StackCallNode(operation, width, arguments)) {
        Operation = operation;
        Width = width;
    }

    /// <summary>The stack operation of the call.</summary>
    public StackOperation Operation { get; }

    /// <summary>The stack width of the call.</summary>
    public BitWidth Width { get; }

    /// <inheritdoc />
    public override MethodCallValueNode WithArguments(IVisitableAstNode[] arguments) =>
        new StackCallValueNode(Operation, Width, arguments);
}
