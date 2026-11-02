namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A generated near or far return helper invocation (<c>NearRet</c> / <c>FarRet</c>). Carries the
/// distance and the return width as fields; the method name is computed by
/// <see cref="HelperCallNames"/>.
/// </summary>
internal sealed record ProcedureReturnHelperNode : MethodCallNode {
    /// <summary>Creates the return helper invocation.</summary>
    /// <param name="distance">Whether the return is near or far.</param>
    /// <param name="width">The return width; it selects the 16-bit or 32-bit helper.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public ProcedureReturnHelperNode(CallDistance distance, BitWidth width, params IVisitableAstNode[] arguments)
        : base(null, HelperCallNames.ProcedureReturnMethodName(distance, width), arguments) {
        Distance = distance;
        Width = width;
    }

    /// <summary>Whether the return is near or far.</summary>
    public CallDistance Distance { get; }

    /// <summary>The return width.</summary>
    public BitWidth Width { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new ProcedureReturnHelperNode(Distance, Width, arguments);
}
