namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

/// <summary>
/// AST node that holds a reference to an <see cref="FieldWithValue"/> instruction field.
/// Instead of baking the <c>UseValue</c> decision into the AST structure at parse time
/// (choosing between <c>ConstantNode</c> and <c>AbsolutePointerNode</c>), this node defers
/// the decision to each visitor at visit time. This makes the AST structurally stable:
/// <see cref="Feeder.SignatureReducer"/> can set <c>UseValue = false</c> on fields without
/// needing to rebuild the AST.
/// </summary>
public record InstructionFieldNode(DataType DataType, FieldWithValue Field, ulong ConstantValue) : ValueNode(DataType) {

    /// <summary>
    /// Returns the appropriate AST node based on the current value of <see cref="FieldWithValue.UseValue"/>.
    /// When <c>UseValue</c> is true, returns a <see cref="ConstantNode"/> with the baked-in value.
    /// When false, returns an <see cref="AbsolutePointerNode"/> that reads from the field's memory location.
    /// This is a computed property: it re-evaluates on every access, so it stays reactive to
    /// <see cref="Feeder.SignatureReducer"/> flipping <c>UseValue</c> after AST construction.
    /// </summary>
    public ValueNode ResolvedNode => Field.UseValue
        ? new ConstantNode(DataType, ConstantValue)
        : new AbsolutePointerNode(DataType, new ConstantNode(DataType.UINT32, Field.PhysicalAddress));

    /// <inheritdoc />
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitInstructionFieldNode(this);
    }
}
