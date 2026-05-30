namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using CfgSelectorNode = Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying.SelectorNode;

/// <summary>
/// Execution-AST marker for a self-modifying-code selector. It carries a reference to its CFG
/// <see cref="CfgSelectorNode"/> so consumers (such as the C# generator's value-returning
/// <c>CSharpAstEmitter</c>) can read the real <c>SuccessorsPerSignature</c> dispatch table; the marker alone
/// is not enough.
/// </summary>
public record SelectorNode : IVisitableAstNode {
    public SelectorNode(CfgSelectorNode cfgSelector) {
        CfgSelector = cfgSelector;
    }

    /// <summary>The CFG selector node this marker stands in for.</summary>
    public CfgSelectorNode CfgSelector { get; }

    public override string ToString() => nameof(SelectorNode);

    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitSelectorNode(this);
    }
}
