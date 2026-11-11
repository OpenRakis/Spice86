namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>A <c>CallLanding[]</c> argument printed as a collection expression of
/// <c>new(new(csN, 0xOOOO), entryDelegate)</c> elements, such as
/// <c>[new(new(cs0, 0x0300), unknown_F000_0300_F0300Entry)]</c>.</summary>
/// <param name="Arms">The observed landings to arm: each address and the generated entry running there.</param>
internal sealed record CallLandingListNode(
    IReadOnlyList<(SegmentedAddress Address, MethodGroupNode Entry)> Arms) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitCallLandingListNode(this);
}
