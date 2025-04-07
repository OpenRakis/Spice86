namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Node that precedes self modifying code divergence point.
/// To decide what is next node in the graph, the only way is to compare discriminators in SuccessorsPerDiscriminator with actual memory content. 
/// </summary>
public class DiscriminatedNode : CfgNode {
    public DiscriminatedNode(SegmentedAddress address) : base(address) {
    }

    public override bool IsLive => true;

    public Dictionary<Discriminator, CfgInstruction> SuccessorsPerDiscriminator { get; private set; } =
        new();

    public override void UpdateSuccessorCache() {
        SuccessorsPerDiscriminator = Successors.OfType<CfgInstruction>()
            .OrderBy(node => node.Discriminator)
            .ToDictionary(node => node.Discriminator);
    }

    public override void Execute(InstructionExecutionHelper helper) {
        foreach (Discriminator discriminator in SuccessorsPerDiscriminator.Keys) {
            int length = discriminator.DiscriminatorValue.Count;
            Span<byte> bytes = helper.Memory.GetSpan((int)Address.Linear, length);
            if (discriminator.SpanEquivalent(bytes)) {
                helper.NextNode = SuccessorsPerDiscriminator[discriminator];
                return;
            }
        }

        helper.NextNode = null;
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.SELECTOR);
    }
}