namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

using CfgSelectorNode = Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying.SelectorNode;

/// <summary>
/// Lifts one <see cref="MethodPlan"/> into an <see cref="IrMethod"/>: per node, the boundary statements
/// (entry event check, speculative guard, instruction-start marker) then the lowered body flattened by
/// <see cref="IrFlattener"/>. The lifted IR is verified with <see cref="IrVerifier"/>; it is the only code
/// that reads CPU AST shapes and helper names.
/// </summary>
internal sealed class IrLifter(
    CfgGeneratorContext context,
    InstructionLowerer lowerer,
    AstInstructionRenderer assemblyRenderer) {

    /// <summary>Lifts every node of <paramref name="method"/>'s emission plans, in order.</summary>
    public IrMethod Lift(MethodPlan method) {
        IrFlattener flattener = new(false);
        HashSet<ICfgNode> entryNodes = method.Entries.Select(entry => entry.Node).ToHashSet();
        Dictionary<ICfgNode, IrBlock> startBlocks = [];
        Dictionary<ICfgNode, IrInstructionShape> shapes = [];
        IReadOnlyList<NodeEmissionPlan> plans = method.NodeEmissionPlans;
        for (int index = 0; index < plans.Count; index++) {
            NodeEmissionPlan plan = plans[index];
            ICfgNode node = plan.Node;
            startBlocks[node] = flattener.StartBlock(node, plan.IsBlockEntry || entryNodes.Contains(node),
                index == 0 ? null : plans[index - 1].Node);
            FlattenBoundary(flattener, plan);
            string localSuffix = node is CfgInstruction instruction
                ? method.GetLocalSuffix(instruction)
                : string.Empty;
            IVisitableAstNode body = BodyOf(plan);
            flattener.Flatten(body, node, localSuffix, IsLocalFallthrough(plans, index, entryNodes));
            if (node is CfgInstruction shaped) {
                shapes[shaped] = ShapeOf(shaped, body);
            }
        }
        foreach (IrFlattener.PendingJump jump in flattener.PendingJumps) {
            jump.From.Terminator = new IrJump(jump.Origin, new IrEdge(startBlocks[jump.Target], false));
        }
        List<IrEntry> entries = method.Entries
            .Select(entry => new IrEntry(entry.Node, startBlocks[entry.Node]))
            .ToList();
        IrMethod lifted = new(method.MethodName, flattener.Blocks, entries,
            new HashSet<ICfgNode>(method.Nodes), shapes);
        foreach (CfgInstruction instruction in method.Nodes.OfType<CfgInstruction>()) {
            lifted.LocalSuffixes[instruction] = method.GetLocalSuffix(instruction);
        }
        foreach (IrBlock block in lifted.Blocks) {
            if (block.Terminator is not IrExit { Transfer: not MoveIpNextNode }) {
                continue;
            }
            foreach (ICfgNode successor in block.Terminator.Origin.Successors) {
                if (!method.Nodes.Contains(successor)
                    || !startBlocks.TryGetValue(successor, out IrBlock? target)
                    || ReferenceEquals(target, block)) {
                    continue;
                }
                (lifted.StructuralSuccessors.TryGetValue(block, out List<IrBlock>? list)
                    ? list
                    : lifted.StructuralSuccessors[block] = []).Add(target);
            }
        }
        IrJumpThreading.Apply(lifted);
        MoveInstructionExitTailsLast(lifted);
        return lifted;
    }

    /// <summary>
    /// An inner block of an instruction that ends in <see cref="IrExit"/> (a throw-if arm, an untested
    /// tail) is moved after the other blocks of its instruction, keeping their relative order. When both
    /// arms of the branch diverge, the structurizer nests the arm later in layout: with the throw arm
    /// last, it nests as a guard and the rest of the method stays flat instead of being nested inside the
    /// inverted condition.
    /// </summary>
    private static void MoveInstructionExitTailsLast(IrMethod method) {
        HashSet<IrBlock> insideOnly = method.BlocksEnteredOnlyThroughInsideEdges();
        HashSet<IrBlock> exitTails = [.. method.Blocks.Where(block => IsExitTail(method, block, insideOnly))];
        if (exitTails.Count == 0) {
            return;
        }
        Dictionary<ICfgNode, int> remaining = [];
        Dictionary<ICfgNode, List<IrBlock>> tails = [];
        foreach (IrBlock block in method.Blocks) {
            if (exitTails.Contains(block)) {
                if (!tails.TryGetValue(block.EntryOrigin, out List<IrBlock>? list)) {
                    list = tails[block.EntryOrigin] = [];
                }
                list.Add(block);
            } else {
                remaining[block.EntryOrigin] = remaining.GetValueOrDefault(block.EntryOrigin) + 1;
            }
        }
        List<IrBlock> reordered = [];
        foreach (IrBlock block in method.Blocks) {
            if (exitTails.Contains(block)) {
                continue;
            }
            reordered.Add(block);
            ICfgNode origin = block.EntryOrigin;
            if (--remaining[origin] == 0 && tails.Remove(origin, out List<IrBlock>? moved)) {
                reordered.AddRange(moved);
            }
        }
        reordered.AddRange(tails.Values.SelectMany(list => list));
        method.Blocks.Clear();
        method.Blocks.AddRange(reordered);
    }

    private static bool IsExitTail(IrMethod method, IrBlock block, HashSet<IrBlock> insideOnly) =>
        !block.StartsInstruction
        && block.Terminator is IrExit
        && method.Entries.All(entry => !ReferenceEquals(entry.Block, block))
        && insideOnly.Contains(block);

    private void FlattenBoundary(IrFlattener flattener, NodeEmissionPlan plan) {
        ICfgNode node = plan.Node;
        if (plan.EmitsExternalEventCheck) {
            flattener.Add(new CheckExternalEventsNode(node.Address), node, string.Empty);
        }
        IVisitableAstNode guard = InstructionBoundary.SpeculativeGuard(context, node);
        if (!IsEmpty(guard)) {
            flattener.Add(guard, node, string.Empty);
        }
        string comment = InstructionBoundary.AsmCommentText(assemblyRenderer, node)
            ?? throw new NotSupportedException($"IR lifting does not support node {node.GetType().FullName} at {node.Address}.");
        flattener.Add(new InstructionStartNode(comment), node, string.Empty);
    }

    private IVisitableAstNode BodyOf(NodeEmissionPlan plan) => plan.Node switch {
        CfgInstruction instruction => lowerer.LowerForIr(instruction, instruction.ExecutionAst,
            plan.EmitsExternalEventCheckAfter),
        CfgSelectorNode selector => selector.ExecutionAst.Accept(lowerer),
        _ => throw new NotSupportedException($"IR lifting does not support node {plan.Node.GetType().FullName} at {plan.Node.Address}.")
    };

    /// <summary>
    /// A goto is a plain fallthrough when its target is the next node in the plan and that node opens no
    /// CFG block and no method entry.
    /// </summary>
    private static Func<ICfgNode, bool> IsLocalFallthrough(IReadOnlyList<NodeEmissionPlan> plans, int index,
        HashSet<ICfgNode> entryNodes) {
        if (index + 1 >= plans.Count) {
            return _ => false;
        }
        NodeEmissionPlan next = plans[index + 1];
        bool nextOpensBoundary = next.IsBlockEntry || entryNodes.Contains(next.Node);
        return target => !nextOpensBoundary && next.Node.Equals(target);
    }

    private static IrInstructionShape ShapeOf(CfgInstruction instruction, IVisitableAstNode body) {
        if (body is BlockNode block && block.Statements.Count > 0 && block.Statements[0] is DivisionNode) {
            return IrInstructionShape.Division;
        }
        InstructionNode display = instruction.DisplayAst;
        return display.Operation switch {
            InstructionOperation.CMPS or InstructionOperation.SCAS
                when display.RepPrefix is RepPrefix.REPE or RepPrefix.REPNE => IrInstructionShape.RepCompare,
            InstructionOperation.XCHG => IrInstructionShape.Exchange,
            InstructionOperation.LES or InstructionOperation.LDS or InstructionOperation.LSS
                or InstructionOperation.LFS or InstructionOperation.LGS => IrInstructionShape.LoadFarPointer,
            _ => IrInstructionShape.None
        };
    }
}
