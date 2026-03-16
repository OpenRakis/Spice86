namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>
/// Represents the entire CPU flags register (EFLAGS/FLAGS) in the AST.
/// Used for instructions like PUSHF/PUSHFD that save the entire flags state.
/// The data type parameter allows specifying whether to access the 16-bit or 32-bit flag register.
/// </summary>
public record FlagRegisterNode : ValueNode {
    /// <summary>
    /// Initializes a new instance with the specified data type.
    /// </summary>
    /// <param name="dataType">The data type (UINT16 for 16-bit flags, UINT32 for 32-bit flags).</param>
    public FlagRegisterNode(DataType dataType) : base(dataType) {
    }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitFlagRegisterNode(this);
    }
}
