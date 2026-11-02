namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>A generated method passed as a delegate: its name when entered at load offset 0, else a lambda applying the offset.</summary>
/// <param name="MethodName">The name of the generated method.</param>
/// <param name="LoadOffset">The load offset the callee is entered with.</param>
internal sealed record MethodGroupNode(string MethodName, int LoadOffset) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitMethodGroupNode(this);
}
