namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Liveness;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using System.Linq;

/// <summary>Builds the <see cref="InstructionFlagFacts"/> of a node: its flag effect plus the places where every flag must be considered live.</summary>
internal static class InstructionFlagFactsBuilder {
    /// <summary>The facts of <paramref name="node"/>; <paramref name="methodNodes"/> are all nodes of its method.</summary>
    public static InstructionFlagFacts Build(ICfgNode node, IReadOnlySet<ICfgNode> methodNodes) =>
        Build(node, node.ExecutionAst, methodNodes);

    /// <summary>The facts of <paramref name="node"/> when it executes <paramref name="executionAst"/> instead of its own execution AST.</summary>
    public static InstructionFlagFacts Build(ICfgNode node, IVisitableAstNode executionAst, IReadOnlySet<ICfgNode> methodNodes) {
        FlagEffect effect = FlagEffectVisitor.Of(executionAst);
        bool allLiveAfter = CallReturnHaltDetectorVisitor.ContainsCallReturnOrHalt(executionAst)
            || node.Successors.Any(successor => !methodNodes.Contains(successor));
        bool allLiveBefore = node is CfgInstruction instruction && HasCpuFaultSuccessor(instruction);
        return new InstructionFlagFacts(effect, allLiveBefore, allLiveAfter);
    }

    private static bool HasCpuFaultSuccessor(CfgInstruction instruction) =>
        instruction.SuccessorsPerType.TryGetValue(InstructionSuccessorType.CpuFault, out ISet<ICfgNode>? successors)
        && successors.Count > 0;
}
