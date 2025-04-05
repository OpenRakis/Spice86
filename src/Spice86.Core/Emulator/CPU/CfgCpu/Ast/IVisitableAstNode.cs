namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast;

public interface IVisitableAstNode {
    public T Accept<T>(IAstVisitor<T> astVisitor);
}