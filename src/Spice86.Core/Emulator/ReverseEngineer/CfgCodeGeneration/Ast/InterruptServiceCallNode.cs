namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// A generated direct interrupt service implementation call
/// (<c>Disk.ReadSectors(dl, al, es, bx, cx, dx)</c>). Printed instead of <c>InterruptCall</c>
/// when every <c>int</c> site of the program on the vector observed only the installed stub and
/// the selector register has a known constant value whose service entry binds an implementation.
/// The omitted interrupt frame is intentional: the bound cores do not depend on it.
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
            ?? throw new InvalidOperationException(
                $"{service.Name} has no implementation to call"), arguments) {
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

    /// <summary>The constant string the call reads at <c>DS:DX</c>, printed as the checked
    /// expected value of the string argument; <see langword="null"/> otherwise.</summary>
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
