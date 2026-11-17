namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Shared.Emulator.Memory;

using CSharpOverrideHelper = Spice86.Core.Emulator.ReverseEngineer.CSharpOverrideHelper;

/// <summary>
/// A generated typed interrupt service call
/// (<c>(AX, CarryFlag) = ServiceCall(cs1, 0x0108, Dos, () => (AX, CarryFlag))</c>). Printed instead
/// of <c>InterruptCall</c> when every <c>int</c> site of the program on the vector observed only
/// the installed stub and the selector register has a known constant value whose service entry
/// binds an implementation. The helper performs the same stack traffic as the interpreted
/// <c>int</c> + stub + <c>iret</c> path.
/// </summary>
internal sealed record InterruptServiceCallNode : MethodCallNode {
    /// <summary>Creates the typed service call.</summary>
    /// <param name="vector">The interrupt vector the call invokes.</param>
    /// <param name="selector">The selector register of the vector's handler, or
    /// <see langword="null"/> for a selector-less handler.</param>
    /// <param name="service">The service entry whose implementation the call invokes.</param>
    /// <param name="receiverPropertyName">The <c>CSharpOverrideHelper</c> property the handler
    /// is reached through.</param>
    /// <param name="arguments">The bound input values, one per
    /// <see cref="InterruptServiceEntry.Inputs"/> binding.</param>
    public InterruptServiceCallNode(byte vector, ServiceSelector? selector,
        InterruptServiceEntry service, string receiverPropertyName,
        params IVisitableAstNode[] arguments)
        : base(receiverPropertyName, service.Implementation?.Name
            ?? (service.Contract.Transfer is ServiceTransferKind.RunsChild
                ? nameof(CSharpOverrideHelper.ExecChild)
                : throw new InvalidOperationException(
                    $"{service.Name} has no implementation to call")), arguments) {
        Vector = vector;
        Selector = selector;
        Service = service;
    }

    /// <summary>The interrupt vector the call invokes.</summary>
    public byte Vector { get; }

    /// <summary>The selector register of the vector's handler, or <see langword="null"/> for a
    /// selector-less handler.</summary>
    public ServiceSelector? Selector { get; }

    /// <summary>The service entry the call invokes.</summary>
    public InterruptServiceEntry Service { get; }

    /// <summary>The selector value the call's service is for.</summary>
    public int SelectorValue => Service.SelectorValue;

    /// <summary>The precise service contract whose effects the call has.</summary>
    public ServiceContract Contract => Service.Contract;

    /// <summary>Whether the call runs the handler's entry bookkeeping
    /// (<see cref="InterruptHandler.OnInterruptEntry"/>): decided at classification time from the
    /// service entry, so a typed call never consults the live selector register.</summary>
    public bool RunsEntryHook { get; init; } = true;

    /// <summary>Whether the typed call pushes and pops the interrupt frame like a real
    /// <c>int</c>: it does only when the program set the current PSP itself through DOS
    /// AH=50h/55h, so a terminate can resume the parent through the frame the call left.
    /// Otherwise the call writes no frame, only runs the entry hook under a lowered SP when
    /// <see cref="RunsEntryHook"/>.</summary>
    public bool WritesFrame { get; init; } = true;

    /// <summary>The constant string the call reads at <c>DS:DX</c>, printed as the checked
    /// expected value of the string argument; <see langword="null"/> otherwise.</summary>
    public string? StringLiteral { get; init; }

    /// <summary>The output lvalues the call's typed return value is assigned to, one per contract
    /// output (<c>AX</c>, <c>CarryFlag</c>, ...). Destinations only: the call writes them, they
    /// are never read as part of the invocation.</summary>
    public IReadOnlyList<ValueNode> Results { get; init; } = [];

    /// <summary>The child program entry addresses discovery observed an EXEC land on: the site's
    /// convention demand unions their entry inputs (<see cref="ServiceTransferKind.RunsChild"/>);
    /// empty for a call that runs no child.</summary>
    public IReadOnlyList<SegmentedAddress> ChildEntries { get; init; } = [];

    /// <summary>The segment the pushed frame must return to, printed as the first
    /// <c>ServiceCall</c> argument.</summary>
    public required IVisitableAstNode ReturnSegment { get; init; }

    /// <summary>The offset the pushed frame must return to, printed as the second
    /// <c>ServiceCall</c> argument.</summary>
    public required IVisitableAstNode ReturnOffset { get; init; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new InterruptServiceCallNode(Vector, Selector, Service,
            PropertyPath ?? string.Empty, arguments) {
            RunsEntryHook = RunsEntryHook,
            WritesFrame = WritesFrame,
            StringLiteral = StringLiteral,
            Results = Results,
            ChildEntries = ChildEntries,
            ReturnSegment = ReturnSegment,
            ReturnOffset = ReturnOffset
        };

    /// <summary>A copy of the call with its read children - the pushed return address and the
    /// input arguments - replaced; the result lvalues are write targets and stay.</summary>
    public InterruptServiceCallNode WithMappedReads(IVisitableAstNode returnSegment,
        IVisitableAstNode returnOffset, IVisitableAstNode[] arguments) =>
        new InterruptServiceCallNode(Vector, Selector, Service,
            PropertyPath ?? string.Empty, arguments) {
            RunsEntryHook = RunsEntryHook,
            WritesFrame = WritesFrame,
            StringLiteral = StringLiteral,
            Results = Results,
            ChildEntries = ChildEntries,
            ReturnSegment = returnSegment,
            ReturnOffset = returnOffset
        };
}
