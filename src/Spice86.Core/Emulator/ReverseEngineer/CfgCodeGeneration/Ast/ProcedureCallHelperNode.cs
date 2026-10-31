namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A generated near or far call helper invocation (<c>NearCall</c> / <c>FarCall</c>). Carries the
/// distance and the pushed-return-address width as fields; the method name is computed by
/// <see cref="HelperCallNames"/>.
/// </summary>
internal sealed record ProcedureCallHelperNode : MethodCallNode {
    /// <summary>Creates the call helper invocation.</summary>
    /// <param name="distance">Whether the call is near or far.</param>
    /// <param name="width">The call width; it selects the 16-bit or 32-bit helper.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public ProcedureCallHelperNode(CallDistance distance, BitWidth width, params IVisitableAstNode[] arguments)
        : base(null, HelperCallNames.ProcedureCallMethodName(distance, width), arguments) {
        Distance = distance;
        Width = width;
    }

    /// <summary>Whether the call is near or far.</summary>
    public CallDistance Distance { get; }

    /// <summary>The call width.</summary>
    public BitWidth Width { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new ProcedureCallHelperNode(Distance, Width, arguments);
}
