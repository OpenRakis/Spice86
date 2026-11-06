namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// A generated typed interrupt service helper invocation (<c>Dos.PrintString(cs1, 0x0116, DS, DX)</c>).
/// Printed instead of <c>InterruptCall</c> when every observed target of the <c>int</c> site is the
/// installed stub of its vector and the selector register has a known constant value with a precise
/// service contract.
/// </summary>
internal sealed record InterruptServiceCallNode : MethodCallNode {
    /// <summary>Creates the typed helper invocation.</summary>
    /// <param name="vector">The interrupt vector the call invokes.</param>
    /// <param name="selector">The selector register of the vector's handler, or
    /// <see langword="null"/> for a selector-less handler.</param>
    /// <param name="service">The service entry the call prints and whose effects it has.</param>
    /// <param name="groupPropertyName">The <c>CSharpOverrideHelper</c> property the helper group
    /// is reached through.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public InterruptServiceCallNode(byte vector, ServiceSelector? selector,
        InterruptServiceEntry service, string groupPropertyName,
        params IVisitableAstNode[] arguments)
        : base(groupPropertyName, service.Name, arguments) {
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

    /// <summary>The string the call reads at <c>DS:DX</c>, printed as the last argument
    /// when it is a constant of the loaded program; <see langword="null"/> otherwise.</summary>
    public string? StringLiteral { get; init; }

    /// <summary>The output lvalues the call's typed return value is assigned to, one per contract
    /// output (<c>AX</c>, <c>CarryFlag</c>, ...). Destinations only: the call writes them, they
    /// are never read as part of the invocation.</summary>
    public IReadOnlyList<ValueNode> Results { get; init; } = [];

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new InterruptServiceCallNode(Vector, Selector, Service,
            PropertyPath ?? string.Empty, arguments) {
            StringLiteral = StringLiteral,
            Results = Results
        };
}
