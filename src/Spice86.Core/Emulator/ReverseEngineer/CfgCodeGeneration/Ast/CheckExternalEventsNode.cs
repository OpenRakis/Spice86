namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>A <c>CheckExternalEvents</c> helper call pausing at a resume point.</summary>
/// <param name="ResumeAddress">The address the emulator resumes at after handling external events.</param>
internal sealed record CheckExternalEventsNode(SegmentedAddress ResumeAddress) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitCheckExternalEventsNode(this);
}
