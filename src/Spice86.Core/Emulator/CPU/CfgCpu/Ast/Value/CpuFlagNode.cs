namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>
/// Represents a CPU flag reference in the AST.
/// </summary>
/// <param name="flagMask">The flag mask constant from <see cref="Flags"/> (e.g., Flags.Carry, Flags.Zero).</param>
public class CpuFlagNode(ushort flagMask) : ValueNode(DataType.BOOL) {
    /// <summary>
    /// Gets the flag mask that identifies which CPU flag this node represents.
    /// </summary>
    public ushort FlagMask { get; } = flagMask;
    
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitCpuFlagNode(this);
    }
}
