namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// A pure select between two values: <c>Condition ? WhenTrue : WhenFalse</c>. Only the chosen arm is
/// evaluated, like C# <c>?:</c>. The builders only create arms that are pure values (constants,
/// registers, flags, variables and operators on them); nothing that can fault or read memory.
/// </summary>
public record ConditionalValueNode(DataType DataType, ValueNode Condition, ValueNode WhenTrue,
    ValueNode WhenFalse) : ValueNode(DataType) {
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitConditionalValueNode(this);
    }
}
