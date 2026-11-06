namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

/// <summary>A generated <c>InterruptCall</c> helper invocation; the vector stays an argument.</summary>
internal sealed record InterruptCallHelperNode : MethodCallNode {
    /// <summary>Creates the helper invocation.</summary>
    /// <param name="kind">How the site ends up printed.</param>
    /// <param name="service">The installed service vector of the site, non-null only for
    /// <see cref="InterruptSiteKind.BuiltInNotAnalysed"/> and the refined kinds
    /// (<see cref="InterruptSiteKind.SelectorUnknown"/>, <see cref="InterruptSiteKind.NoContract"/>,
    /// <see cref="InterruptSiteKind.BuiltInDispatch"/>) the service-call pass prints.</param>
    /// <param name="selectorValue">The selector value the site was classified with, when the
    /// selector is a known constant; null when it is not.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public InterruptCallHelperNode(InterruptSiteKind kind, InterruptServiceVector? service,
        int? selectorValue, params IVisitableAstNode[] arguments)
        : base(null, HelperCallNames.InterruptCallMethodName, arguments) {
        Kind = kind;
        Service = service;
        SelectorValue = selectorValue;
    }

    /// <summary>How the site ends up printed.</summary>
    public InterruptSiteKind Kind { get; }

    /// <summary>The installed service vector of the site.</summary>
    public InterruptServiceVector? Service { get; }

    /// <summary>The selector value the site was classified with, when known.</summary>
    public int? SelectorValue { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new InterruptCallHelperNode(Kind, Service, SelectorValue, arguments);
}
