namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
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
    /// <param name="selectorValue">The selector value the call's contract is for (0 for a
    /// selector-less handler).</param>
    /// <param name="contract">The precise service contract the call prints and whose effects it
    /// has.</param>
    /// <param name="groupPropertyName">The <c>CSharpOverrideHelper</c> property the helper group
    /// is reached through.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public InterruptServiceCallNode(byte vector, ServiceSelector? selector, int selectorValue,
        ServiceContract contract, string groupPropertyName,
        params IVisitableAstNode[] arguments)
        : base(groupPropertyName, contract.Name, arguments) {
        Vector = vector;
        Selector = selector;
        SelectorValue = selectorValue;
        Contract = contract;
    }

    /// <summary>The interrupt vector the call invokes.</summary>
    public byte Vector { get; }

    /// <summary>The selector register of the vector's handler, or <see langword="null"/> for a
    /// selector-less handler.</summary>
    public ServiceSelector? Selector { get; }

    /// <summary>The selector value the call's contract is for.</summary>
    public int SelectorValue { get; }

    /// <summary>The precise service contract whose effects the call has.</summary>
    public ServiceContract Contract { get; }

    /// <summary>The string the call reads at <c>DS:DX</c>, printed as the last argument
    /// when it is a constant of the loaded program; <see langword="null"/> otherwise.</summary>
    public string? StringLiteral { get; init; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new InterruptServiceCallNode(Vector, Selector, SelectorValue, Contract,
            PropertyPath ?? string.Empty, arguments) {
            StringLiteral = StringLiteral
        };
}
