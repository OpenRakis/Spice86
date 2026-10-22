namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>A run of statements whose local variables carry the given suffix; printing it activates that suffix.</summary>
/// <param name="Node">The CFG node the run belongs to.</param>
/// <param name="LocalVariableSuffix">The suffix appended to local variable names while the body prints.</param>
/// <param name="Body">The statements printed with the suffix active.</param>
internal sealed record InstructionBoundaryNode(ICfgNode Node, string LocalVariableSuffix, BlockNode Body) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitInstructionBoundaryNode(this);
}
