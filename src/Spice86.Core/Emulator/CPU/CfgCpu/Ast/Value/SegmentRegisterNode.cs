namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

public class SegmentRegisterNode(int registerIndex) : RegisterNode(DataType.UINT16, registerIndex) {
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitSegmentRegisterNode(this);
    }
}