namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Shared.Emulator.Memory;

/// <summary>A <c>HltWait</c> helper call: HLT with interrupts enabled, waiting for the hardware interrupt
/// that resumes the CPU.</summary>
/// <param name="ResumeAddress">The address the emulator resumes at after the interrupt is delivered.</param>
/// <param name="HardwareInterruptEffect">The registers the program's hardware interrupt handlers
/// may read and may leave changed.</param>
internal sealed record HltWaitNode(SegmentedAddress ResumeAddress,
    CallEffect HardwareInterruptEffect) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitHltWaitNode(this);
}
