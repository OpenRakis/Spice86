namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using CfgSelectorNode = Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying.SelectorNode;

/// <summary>The boundary pieces an emitted instruction shares between the method emitter and the IR lifter:
/// the speculative-execution guard and the asm comment text.</summary>
internal static class InstructionBoundary {
    /// <summary>
    /// Builds a <c>VerifySpeculativeEntryOrFail</c> guard for a single speculative instruction, or the empty
    /// sequence for a non-speculative one.
    /// </summary>
    /// <remarks>
    /// The guard re-reads the instruction's bytes from memory immediately before its body executes and fails as
    /// untested if they no longer match the signature decoded at exploration time. Emitting one guard per
    /// speculative instruction (rather than a single block-entry guard covering the whole run) is what lets
    /// the generated code detect self-modifying code that an earlier instruction in the same block performs
    /// against a later speculative instruction: an entry-only guard runs before any instruction executes and
    /// so cannot observe such a mutation.
    /// </remarks>
    public static IVisitableAstNode SpeculativeGuard(CfgGeneratorContext context, ICfgNode node) {
        if (node is not CfgInstruction { IsSpeculative: true } speculativeInstruction) {
            return CodeGenerationAst.Empty();
        }

        IReadOnlyList<byte?> signatureValue = speculativeInstruction.Signature.SignatureValue;
        if (signatureValue.Count == 0) {
            return CodeGenerationAst.Empty();
        }
        string fieldName = context.GetSignatureField(speculativeInstruction);
        return new SpeculativeGuardNode(speculativeInstruction.Address, fieldName);
    }

    /// <summary>The asm comment text identifying <paramref name="node"/>, or <c>null</c> for a node kind
    /// without one.</summary>
    public static string? AsmCommentText(AstInstructionRenderer assemblyRenderer, ICfgNode node) => node switch {
        CfgInstruction instruction => $"{instruction.Address} {instruction.DisplayAst.Accept(assemblyRenderer)}",
        CfgSelectorNode selectorNode => $"{selectorNode.Address} selector",
        _ => null
    };
}
