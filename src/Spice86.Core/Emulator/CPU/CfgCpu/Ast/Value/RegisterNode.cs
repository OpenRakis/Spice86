namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

public class RegisterNode(DataType dataType, int registerIndex) : ValueNode(dataType) {
    public int RegisterIndex { get; } = registerIndex;
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitRegisterNode(this);
    }
}