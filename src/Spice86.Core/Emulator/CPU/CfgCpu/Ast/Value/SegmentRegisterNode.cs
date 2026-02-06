namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

public record SegmentRegisterNode(int RegisterIndex) : RegisterNode(DataType.UINT16, RegisterIndex) {
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitSegmentRegisterNode(this);
    }
}