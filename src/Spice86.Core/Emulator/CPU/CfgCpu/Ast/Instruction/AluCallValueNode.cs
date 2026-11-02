namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A value-returning ALU helper call. Carries the operation and width as fields; the property path and
/// method name are computed by <see cref="HelperCallNames"/>.
/// </summary>
public sealed record AluCallValueNode : MethodCallValueNode {
    /// <summary>
    /// Initializes a new instance of the AluCallValueNode class.
    /// </summary>
    /// <param name="dataType">The data type of the returned value.</param>
    /// <param name="operation">The ALU operation of the call.</param>
    /// <param name="width">The ALU width of the call.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public AluCallValueNode(DataType dataType, AluOperation operation, BitWidth width,
            params IVisitableAstNode[] arguments)
        : base(dataType, new AluCallNode(operation, width, arguments)) {
        Operation = operation;
        Width = width;
    }

    /// <summary>The ALU operation of the call.</summary>
    public AluOperation Operation { get; }

    /// <summary>The ALU width of the call.</summary>
    public BitWidth Width { get; }

    /// <inheritdoc />
    public override MethodCallValueNode WithArguments(IVisitableAstNode[] arguments) =>
        new AluCallValueNode(DataType, Operation, Width, arguments);
}
