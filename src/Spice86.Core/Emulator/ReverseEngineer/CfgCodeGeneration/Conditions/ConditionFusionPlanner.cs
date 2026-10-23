namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Conditions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Liveness;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

using System.Linq;

/// <summary>
/// Finds, for each <c>jcc</c>, the setter of its flags: the nearest earlier node of its block that writes a
/// flag the jump reads. When the instructions in between cannot change the outcome, it rewrites the pair: the
/// setter loses its ALU call, the jump tests the recovered relation. A jump after an ALU operation on a
/// register tests the assigned result, and the setter is kept.
/// </summary>
internal static class ConditionFusionPlanner {
    /// <summary>The fusion of <paramref name="method"/> given its flag liveness.</summary>
    public static ConditionFusion Plan(MethodPlan method, FlagLiveness liveness) =>
        Plan(method.NodeEmissionPlans, method.Entries.Select(entry => entry.Node).ToHashSet(), liveness.LiveAfter);

    /// <summary>The fusion of the nodes of <paramref name="plans"/>, in emission order.</summary>
    public static ConditionFusion Plan(IReadOnlyList<NodeEmissionPlan> plans, IReadOnlySet<ICfgNode> entryNodes,
        Func<ICfgNode, StatusFlags> liveAfter) {
        Dictionary<ICfgNode, IVisitableAstNode> rewritten = new();
        for (int jumpIndex = 0; jumpIndex < plans.Count; jumpIndex++) {
            NodeEmissionPlan jumpPlan = plans[jumpIndex];
            if (jumpPlan.Node is not CfgInstruction jump || jump.ExecutionAst is not IfElseNode jumpIf
                || !CanRewriteJump(jumpPlan, entryNodes) || HasCpuFaultSuccessor(jump)) {
                continue;
            }
            int setterIndex = FindSetter(plans, jumpIndex, FlagEffectVisitor.Of(jumpIf).Reads);
            if (setterIndex < 0) {
                continue;
            }
            NodeEmissionPlan setterPlan = plans[setterIndex];
            List<NodeEmissionPlan> between = plans.Skip(setterIndex + 1).Take(jumpIndex - setterIndex - 1).ToList();
            if (setterPlan.Node is not CfgInstruction setter || setter.ExecutionAst is not BlockNode setterBlock
                || setterPlan.EmitsExternalEventCheckAfter || HasCpuFaultSuccessor(setter)
                || !AreQuiet(between, entryNodes) || !SetterWritesWhatJumpReads(setterBlock, jumpIf)) {
                continue;
            }
            GeneralRegisters writtenBetween = between.Aggregate(GeneralRegisters.None,
                (written, plan) => written | RegisterWriteVisitor.Of(plan.Node.ExecutionAst));
            bool flagsReadBetween = between.Any(plan => FlagEffectVisitor.Of(plan.Node.ExecutionAst).Reads != StatusFlags.None);
            ValueNode? fusedCondition = ConditionRecovery.TryRecover(setterBlock, jumpIf.Condition);
            if (fusedCondition is not null && liveAfter(jump) == StatusFlags.None && !flagsReadBetween
                && IsStable(fusedCondition, between.Count, writtenBetween)) {
                rewritten[setter] = new BlockNode(setterBlock.Statements[^1]);
                rewritten[jump] = new IfElseNode(fusedCondition, jumpIf.TrueCase, jumpIf.FalseCase);
                continue;
            }
            ValueNode? resultCondition = ConditionRecovery.TryRecoverFromResult(setterBlock, jumpIf.Condition);
            if (resultCondition is not null && IsStable(resultCondition, between.Count, writtenBetween)) {
                rewritten[jump] = new IfElseNode(resultCondition, jumpIf.TrueCase, jumpIf.FalseCase);
            }
        }
        return new ConditionFusion(rewritten);
    }

    private static bool CanRewriteJump(NodeEmissionPlan jumpPlan, IReadOnlySet<ICfgNode> entryNodes) =>
        !jumpPlan.IsBlockEntry && !jumpPlan.EmitsExternalEventCheck && !entryNodes.Contains(jumpPlan.Node);

    /// <summary>The index of the nearest earlier node of the jump's block that may write a flag the jump reads, or -1.</summary>
    private static int FindSetter(IReadOnlyList<NodeEmissionPlan> plans, int jumpIndex, StatusFlags jumpReads) {
        if (jumpReads == StatusFlags.None) {
            return -1;
        }
        for (int index = jumpIndex - 1; index >= 0; index--) {
            if (!ReferenceEquals(plans[index].Block, plans[jumpIndex].Block)) {
                return -1;
            }
            if ((FlagEffectVisitor.Of(plans[index].Node.ExecutionAst).MayWrite & jumpReads) != StatusFlags.None) {
                return index;
            }
        }
        return -1;
    }

    private static bool AreQuiet(IReadOnlyList<NodeEmissionPlan> between, IReadOnlySet<ICfgNode> entryNodes) =>
        between.All(plan => plan.Node is CfgInstruction instruction
            && !entryNodes.Contains(instruction)
            && !plan.EmitsExternalEventCheck
            && !plan.EmitsExternalEventCheckAfter
            && !HasCpuFaultSuccessor(instruction));

    /// <summary>Whether <paramref name="condition"/> evaluated at the jump gives the value it had right after the setter.</summary>
    private static bool IsStable(ValueNode condition, int intermediateCount, GeneralRegisters writtenBetween) {
        if (intermediateCount == 0) {
            return true;
        }
        GeneralRegisters? read = RegistersReadWithoutMemory(condition);
        return read is not null && (read.Value & writtenBetween) == GeneralRegisters.None;
    }

    /// <summary>The registers <paramref name="node"/> reads, or <c>null</c> when it reads memory or contains another kind of node.</summary>
    private static GeneralRegisters? RegistersReadWithoutMemory(ValueNode node) {
        switch (node) {
            case SegmentRegisterNode:
                return null;
            case RegisterNode register:
                return RegisterWriteVisitor.Covering(register);
            case ConstantNode:
                return GeneralRegisters.None;
            case InstructionFieldNode field:
                return field.ResolvedNode is ConstantNode ? GeneralRegisters.None : null;
            case TypeConversionNode conversion:
                return RegistersReadWithoutMemory(conversion.Value);
            case BinaryOperationNode binary:
                GeneralRegisters? left = RegistersReadWithoutMemory(binary.Left);
                GeneralRegisters? right = RegistersReadWithoutMemory(binary.Right);
                if (left is null || right is null) {
                    return null;
                }
                return left.Value | right.Value;
            default:
                return null;
        }
    }

    private static bool SetterWritesWhatJumpReads(IVisitableAstNode setterAst, IVisitableAstNode jumpAst) =>
        (FlagEffectVisitor.Of(jumpAst).Reads & ~FlagEffectVisitor.Of(setterAst).MustWrite) == StatusFlags.None;

    private static bool HasCpuFaultSuccessor(CfgInstruction instruction) =>
        instruction.SuccessorsPerType.TryGetValue(InstructionSuccessorType.CpuFault, out ISet<ICfgNode>? successors)
        && successors.Count > 0;
}
