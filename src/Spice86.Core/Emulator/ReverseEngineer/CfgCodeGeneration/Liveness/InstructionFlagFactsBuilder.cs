namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Liveness;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>Builds the <see cref="InstructionFlagFacts"/> of a node: its flag effect plus the places where every flag must be considered live.</summary>
internal sealed class InstructionFlagFactsBuilder(CfgGeneratorContext context) {
    /// <summary>The facts of <paramref name="node"/>; <paramref name="methodNodes"/> are all nodes of its method.</summary>
    public InstructionFlagFacts Build(ICfgNode node, IReadOnlySet<ICfgNode> methodNodes) =>
        Build(node, node.ExecutionAst, methodNodes);

    /// <summary>The facts of <paramref name="node"/> when it executes <paramref name="executionAst"/> instead of its own execution AST.</summary>
    public InstructionFlagFacts Build(ICfgNode node, IVisitableAstNode executionAst, IReadOnlySet<ICfgNode> methodNodes) {
        FlagEffect effect = FlagEffectVisitor.Of(executionAst);
        bool allLiveAfter = CallReturnHaltDetectorVisitor.ContainsCallReturnOrHalt(executionAst)
            || node.Successors.Any(successor => !methodNodes.Contains(successor))
            || HasSynthesizedEdge(node);
        bool allLiveBefore = node is CfgInstruction instruction && HasCpuFaultSuccessor(instruction);
        return new InstructionFlagFacts(effect, allLiveBefore, allLiveAfter);
    }

    private bool HasSynthesizedEdge(ICfgNode node) {
        if (node is not CfgInstruction instruction) {
            return false;
        }
        foreach (SegmentedAddress target in instruction.StaticSuccessorAddresses) {
            if (!HasObservedNormalSuccessorAt(instruction, target)
                && context.TryResolveSamePartitionBlockEntry(instruction, target) is not null) {
                return true;
            }
        }
        return false;
    }

    private static bool HasObservedNormalSuccessorAt(CfgInstruction instruction, SegmentedAddress target) =>
        instruction.SuccessorsPerType.TryGetValue(InstructionSuccessorType.Normal, out ISet<ICfgNode>? successors)
        && successors.Any(successor => successor.Address == target);

    private static bool HasCpuFaultSuccessor(CfgInstruction instruction) =>
        instruction.SuccessorsPerType.TryGetValue(InstructionSuccessorType.CpuFault, out ISet<ICfgNode>? successors)
        && successors.Count > 0;
}
