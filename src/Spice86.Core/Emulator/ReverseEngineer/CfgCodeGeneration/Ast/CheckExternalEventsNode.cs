namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Shared.Emulator.Memory;

/// <summary>A <c>CheckExternalEvents</c> helper call pausing at a resume point.</summary>
/// <param name="ResumeAddress">The address the emulator resumes at after handling external events.</param>
/// <param name="HardwareInterruptEffect">The registers the program's hardware interrupt handlers
/// may read and may leave changed.</param>
internal sealed record CheckExternalEventsNode(SegmentedAddress ResumeAddress,
    CallEffect HardwareInterruptEffect) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitCheckExternalEventsNode(this);
}
