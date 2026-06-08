namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using AstSelectorNode = Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow.SelectorNode;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Node that precedes self modifying code divergence point.
/// To decide what is next node in the graph, the only way is to compare signatures in SuccessorsPerSignature with actual memory content. 
/// </summary>
public class SelectorNode(int id, SegmentedAddress address) : CfgNode(id, address, null) {
    public override bool IsLive => true;

    /// <inheritdoc />
    /// <remarks>
    /// A <see cref="SelectorNode"/> is, by definition, always a block terminator: it dispatches
    /// between multiple instruction variants at the same address and therefore must end its
    /// containing <see cref="CfgBlock"/>.
    /// </remarks>
    public override bool IsBlockTerminator => true;

    /// <inheritdoc />
    /// <remarks>
    /// A <see cref="SelectorNode"/> is never a block starter (the explicit starter flag is reserved
    /// for instructions whose parser tags them as such, e.g. <c>CLI</c>). The base default is also
    /// <c>false</c>; the override is kept here for clarity and to mirror <see cref="IsBlockTerminator"/>.
    /// </remarks>
    public override bool IsBlockStarter => false;

    /// <inheritdoc />
    /// <remarks>
    /// Set exclusively by <see cref="Linker.NodeLinker"/>.
    /// </remarks>
    public override CfgBlock? ContainingBlock { get; set; }

    public Dictionary<Signature, CfgInstruction> SuccessorsPerSignature { get; private set; } =
        new();

    public override void UpdateSuccessorCache() {
        SuccessorsPerSignature = Successors.OfType<CfgInstruction>()
            .OrderBy(node => node.Signature)
            .ToDictionary(node => node.Signature);
    }
    public override ICfgNode? GetNextSuccessor(InstructionExecutionHelper helper) {
        foreach (Signature signature in SuccessorsPerSignature.Keys) {
            int length = signature.SignatureValue.Count;
            IList<byte> bytes = helper.Memory.GetSlice((int)Address.Linear, length);
            if (signature.ListEquivalent(bytes)) {
                return SuccessorsPerSignature[signature];
            }
        }

        return null;
    }

    public override InstructionNode DisplayAst =>
        new InstructionNode(InstructionOperation.SELECTOR);

    public override IVisitableAstNode ExecutionAst =>
        new AstSelectorNode(this);
}