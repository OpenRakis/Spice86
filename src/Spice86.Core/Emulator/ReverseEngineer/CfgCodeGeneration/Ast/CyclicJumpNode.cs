namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>A cross-partition jump that can cycle: it bounces through the entry dispatcher when the target method says so, otherwise returns the dispatcher.</summary>
/// <param name="MethodName">The generated method the jump can re-enter.</param>
/// <param name="LoadOffset">The load offset the jump enters <paramref name="MethodName"/> with.</param>
/// <param name="SingleEntry"><c>true</c> when the method has one entry: the dispatcher target is its cached
/// <c>Entry(M)</c> wrapper and the trampoline never reloads <c>loadOffset</c>.</param>
internal sealed record CyclicJumpNode(string MethodName, int LoadOffset, bool SingleEntry) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitCyclicJumpNode(this);
}
