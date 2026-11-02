namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>A cross-partition jump that can cycle: it bounces through the entry dispatcher when the target method says so, otherwise returns the dispatcher.</summary>
/// <param name="MethodName">The generated method the jump can re-enter.</param>
/// <param name="LoadOffset">The load offset the jump enters <paramref name="MethodName"/> with.</param>
internal sealed record CyclicJumpNode(string MethodName, int LoadOffset) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitCyclicJumpNode(this);
}
