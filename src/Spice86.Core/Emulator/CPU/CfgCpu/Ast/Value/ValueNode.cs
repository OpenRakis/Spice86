namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

public abstract class ValueNode(DataType dataType) : IVisitableAstNode {

    public DataType DataType { get; } = dataType;
    
    public abstract T Accept<T>(IAstVisitor<T> astVisitor);
}