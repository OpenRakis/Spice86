namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

public abstract record ValueNode(DataType DataType) : IVisitableAstNode {
    public abstract T Accept<T>(IAstVisitor<T> astVisitor);
}