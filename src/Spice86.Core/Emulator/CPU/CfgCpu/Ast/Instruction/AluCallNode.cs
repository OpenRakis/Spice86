namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A void ALU helper call (the <c>AluN.UpdateFlags</c> statement). Carries the operation and width as
/// fields; the property path and method name are computed by <see cref="HelperCallNames"/>.
/// </summary>
public sealed record AluCallNode : MethodCallNode {
    /// <summary>
    /// Initializes a new instance of the AluCallNode class.
    /// </summary>
    /// <param name="operation">The ALU operation of the call.</param>
    /// <param name="width">The ALU width of the call.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public AluCallNode(AluOperation operation, BitWidth width, params IVisitableAstNode[] arguments)
        : base(HelperCallNames.AluPropertyPath(width), HelperCallNames.AluMethodName(operation), arguments) {
        Operation = operation;
        Width = width;
    }

    /// <summary>The ALU operation of the call.</summary>
    public AluOperation Operation { get; }

    /// <summary>The ALU width of the call.</summary>
    public BitWidth Width { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new AluCallNode(Operation, Width, arguments);
}
