namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A bit scan helper call (<c>BitScanForwardN</c> / <c>BitScanReverseN</c>). Carries the scan
/// direction as a field; the method name is computed by <see cref="HelperCallNames"/>.
/// </summary>
public sealed record BitScanCallNode : MethodCallValueNode {
    /// <summary>
    /// Initializes a new instance of the BitScanCallNode class.
    /// </summary>
    /// <param name="dataType">The data type of the scanned value and the returned index.</param>
    /// <param name="direction">The scan direction.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public BitScanCallNode(DataType dataType, BitScanDirection direction, params IVisitableAstNode[] arguments)
        : base(dataType,
            new MethodCallNode(null, HelperCallNames.BitScanMethodName(direction, dataType.BitWidth), arguments)) {
        Direction = direction;
    }

    /// <summary>The scan direction.</summary>
    public BitScanDirection Direction { get; }

    /// <inheritdoc />
    public override MethodCallValueNode WithArguments(IVisitableAstNode[] arguments) =>
        new BitScanCallNode(DataType, Direction, arguments);
}
