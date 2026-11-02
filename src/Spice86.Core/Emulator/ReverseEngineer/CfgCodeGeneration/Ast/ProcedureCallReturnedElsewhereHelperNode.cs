namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A generated <c>&lt;procedure call helper&gt;ReturnedElsewhere</c> invocation: it runs the call like
/// <see cref="ProcedureCallHelperNode"/> and reports whether the call ended at one of the redirected
/// return addresses passed as its last argument. Carries the distance and the pushed-return-address
/// width as fields; the method name is computed by <see cref="HelperCallNames"/>.
/// </summary>
internal sealed record ProcedureCallReturnedElsewhereHelperNode : MethodCallValueNode {
    /// <summary>Creates the helper invocation.</summary>
    /// <param name="distance">Whether the call is near or far.</param>
    /// <param name="width">The call width; it selects the 16-bit or 32-bit helper.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public ProcedureCallReturnedElsewhereHelperNode(CallDistance distance, BitWidth width, params IVisitableAstNode[] arguments)
        : base(DataType.BOOL,
            new MethodCallNode(null, HelperCallNames.ProcedureCallReturnedElsewhereMethodName(distance, width), arguments)) {
        Distance = distance;
        Width = width;
    }

    /// <summary>Whether the call is near or far.</summary>
    public CallDistance Distance { get; }

    /// <summary>The call width.</summary>
    public BitWidth Width { get; }

    /// <inheritdoc />
    public override MethodCallValueNode WithArguments(IVisitableAstNode[] arguments) =>
        new ProcedureCallReturnedElsewhereHelperNode(Distance, Width, arguments);
}
