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
public class SelectorNode(SegmentedAddress address) : CfgNode(address, null) {
    public override bool IsLive => true;

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
        new AstSelectorNode();
}