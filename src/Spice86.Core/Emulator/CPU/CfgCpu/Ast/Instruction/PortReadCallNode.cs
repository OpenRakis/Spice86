namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A port read helper call (<c>InN(port)</c>). Carries the port width as a field; the method name is
/// computed by <see cref="HelperCallNames"/>.
/// </summary>
public sealed record PortReadCallNode : MethodCallValueNode {
    /// <summary>
    /// Initializes a new instance of the PortReadCallNode class.
    /// </summary>
    /// <param name="width">The port width; the returned value is its unsigned type.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public PortReadCallNode(BitWidth width, params IVisitableAstNode[] arguments)
        : base(DataType.UnsignedFromBitWidth(width),
            new MethodCallNode(null, HelperCallNames.PortMethodName(PortDirection.In, width), arguments)) {
        Width = width;
    }

    /// <summary>The port width of the call.</summary>
    public BitWidth Width { get; }

    /// <inheritdoc />
    public override MethodCallValueNode WithArguments(IVisitableAstNode[] arguments) =>
        new PortReadCallNode(Width, arguments);
}
