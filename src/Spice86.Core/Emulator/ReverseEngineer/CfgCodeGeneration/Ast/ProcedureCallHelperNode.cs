namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
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

    /// <summary>Whether the call's only observed target is the one <see cref="MethodCallNode.Arguments"/>
    /// names: a dispatch over several observed targets is not.</summary>
    public bool SingleObservedTarget { get; init; }

    /// <summary>Whether a continuation was observed after the call during discovery.</summary>
    public bool ObservedContinuation { get; init; }

    /// <summary>Whether discovery observed landings mid-call; such calls keep the untyped helper
    /// form (the landings the wiring arms for the expected return).</summary>
    public bool ObservedLandings { get; init; }

    /// <summary>The callee-convention summary the call provably has, or
    /// <see langword="null"/> when the call must keep <see cref="IrEffects.Unknown"/>.</summary>
    public IrEffects? Summary { get; init; }

    /// <summary>The <see cref="CpuValues"/> the last-but-one arguments of a typed call cover: the
    /// operand nodes the callee's parameters were attached as, just before the
    /// <see cref="MethodGroupNode"/>. <see cref="CpuValues.None"/> for a call without operands -
    /// untyped calls keep <see cref="Summary"/>'s register reads because their callee reads live
    /// machine state; only a typed call may drop them from the summary reads, its callee receives
    /// the operands as C# arguments.</summary>
    public CpuValues TypedArgumentMask { get; init; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new ProcedureCallHelperNode(Distance, Width, arguments) {
            SingleObservedTarget = SingleObservedTarget,
            ObservedContinuation = ObservedContinuation,
            ObservedLandings = ObservedLandings,
            Summary = Summary,
            TypedArgumentMask = TypedArgumentMask
        };
}
