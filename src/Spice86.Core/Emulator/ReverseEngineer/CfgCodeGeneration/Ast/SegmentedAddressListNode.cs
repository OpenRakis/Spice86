namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>A <c>SegmentedAddress[]</c> argument printed as a collection expression of
/// <c>new(csN, 0xOOOO)</c> elements, such as <c>[new(cs3, 0x0C21)]</c>.</summary>
/// <param name="Addresses">The addresses to list.</param>
internal sealed record SegmentedAddressListNode(IReadOnlyList<SegmentedAddress> Addresses) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitSegmentedAddressListNode(this);
}
