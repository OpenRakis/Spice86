namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

public abstract record ValueNode(DataType DataType) : IVisitableAstNode {
    /// <summary>
    /// The instruction a branch condition or a switch value of generated code comes from, or <c>null</c> when
    /// none was recorded. Only the code generator sets it, and only its branch probes read it. It is set on
    /// the node in place, so the tables the generator keys by node instance keep working, and it takes no
    /// part in equality.
    /// </summary>
    internal BranchOrigin? BranchOrigin { get; set; }

    public abstract T Accept<T>(IAstVisitor<T> astVisitor);

    /// <summary>Record equality on the node type and <see cref="DataType"/>, without
    /// <see cref="BranchOrigin"/>.</summary>
    /// <param name="other">The node to compare with.</param>
    public virtual bool Equals(ValueNode? other) =>
        ReferenceEquals(this, other)
        || (other is not null && EqualityContract == other.EqualityContract
            && EqualityComparer<DataType>.Default.Equals(DataType, other.DataType));

    /// <summary>The record hash of the node type and <see cref="DataType"/>, without
    /// <see cref="BranchOrigin"/>.</summary>
    public override int GetHashCode() => HashCode.Combine(EqualityContract, DataType);
}
