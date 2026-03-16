namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>
/// Represents a CPU flag reference in the AST.
/// </summary>
/// <param name="FlagMask">The flag mask constant from <see cref="Flags"/> (e.g., Flags.Carry, Flags.Zero).</param>
public record CpuFlagNode(ushort FlagMask) : ValueNode(DataType.BOOL) {
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitCpuFlagNode(this);
    }
}
