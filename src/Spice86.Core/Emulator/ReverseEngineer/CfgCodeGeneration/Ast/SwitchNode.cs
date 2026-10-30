namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>A dispatch <c>switch</c> over a runtime value; the printer never adds <c>break;</c>, the builder
/// does. A <see cref="DataType.UINT32"/> <paramref name="Value"/> holds a segmented address and its labels
/// print as <c>0xSSSS_OOOO</c>.</summary>
/// <param name="Value">The expression the <c>switch</c> discriminates on.</param>
/// <param name="Sections">The known-value sections, in print order.</param>
/// <param name="Default">The body of the <c>default</c> label, printed as it is.</param>
internal sealed record SwitchNode(ValueNode Value, IReadOnlyList<SwitchSection> Sections, BlockNode Default) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitSwitchNode(this);
}
