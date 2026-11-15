namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>The byte check of a call instruction whose target an emulator address switcher can
/// rewrite at run time. Printed as
/// <c>if (!SelectorSignatureMatches(segment, offset, field)) throw FailAsUntested("Untested code
/// variant at address");</c>, the same form a selector branch prints for an unobserved variant.</summary>
/// <param name="Address">The address of the patchable call instruction.</param>
/// <param name="SignatureField">The name of the generated field holding the call's observed
/// signature bytes.</param>
internal sealed record PatchedCallGuardNode(SegmentedAddress Address, string SignatureField) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitPatchedCallGuardNode(this);
}
