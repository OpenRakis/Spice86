namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A port write helper call (<c>OutN(port, value)</c>). Carries the port width as a field; the method
/// name is computed by <see cref="HelperCallNames"/>.
/// </summary>
public sealed record PortWriteCallNode : MethodCallNode {
    /// <summary>
    /// Initializes a new instance of the PortWriteCallNode class.
    /// </summary>
    /// <param name="width">The port width of the call.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public PortWriteCallNode(BitWidth width, params IVisitableAstNode[] arguments)
        : base(null, HelperCallNames.PortMethodName(PortDirection.Out, width), arguments) {
        Width = width;
    }

    /// <summary>The port width of the call.</summary>
    public BitWidth Width { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new PortWriteCallNode(Width, arguments);
}
