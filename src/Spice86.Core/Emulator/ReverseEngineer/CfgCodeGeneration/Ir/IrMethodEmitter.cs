namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Emits one generated C# method per CFG partition: the method is lifted to <see cref="IrMethod"/>,
/// run through the IR passes, lowered to <see cref="LoweredBlock{TBlock}"/>s, structured and printed.
/// </summary>
internal sealed class IrMethodEmitter(
    CfgGeneratorContext context,
    CpuFaultWrapper cpuFaultWrapper,
    InstructionLowerer lowerer,
    AstInstructionRenderer assemblyRenderer,
    bool collectCensuses,
    bool verify) {

    private readonly CSharpExpressionPrinter _expressions = new(context.SegmentVariables);
    private readonly IrLifter _lifter = new(context, lowerer, assemblyRenderer);

    /// <summary>Builds the AST of <paramref name="method"/>, prints it as one C# method to
    /// <paramref name="writer"/> and times the lift, each IR pass, the lowering and structuring, and
    /// the printing into <paramref name="timer"/>.</summary>
    /// <returns>The censuses of the <c>goto</c>s, status-flag reads and register webs
    /// the printed method holds, and the lifted IR block and statement counts.</returns>
    public MethodCensus Emit(CSharpSourceWriter writer, MethodPlan method, GenerationStepTimer timer) {
        IrMethod irMethod = timer.Measure("lift", () => _lifter.Lift(method));
        int liftedBlocks = irMethod.Blocks.Count;
        int liftedStatements = irMethod.Blocks.Sum(block => block.Statements.Count);
        IrFullPipelineResult pipeline = IrPassPipeline.RunAll(irMethod, verify, collectCensuses, timer);
        IrSsa ssa = pipeline.Ssa;
        (BlockNode EntryDispatch, BlockNode Body, GotoCensus Census, GotoTargets Targets) structured =
            timer.Measure("lowering and structuring", () => {
                BlockGraph<IrBlock> graph = irMethod.ToStructuringGraph();
                DominatorTree<IrBlock> dominators = DominatorTreeBuilder.BuildFromEntries(graph);
                DepthFirstOrdering<IrBlock> traversal = new([.. graph.EntryBlocks, .. graph.Blocks],
                    graph.GetSuccessors);
                NaturalLoopForest<IrBlock> loops = NaturalLoopForest<IrBlock>.Build(graph,
                    traversal.RetreatingEdges, dominators);
                List<LoweredBlock<IrBlock>> loweredBlocks = [.. IrBlockLowering.Lower(irMethod,
                    irMethod.Blocks, graph, dominators, loops)];
                WrapFaultRuns(loweredBlocks, method);
                Dictionary<ICfgNode, IrBlock> startBlockByNode = irMethod.Blocks
                    .Where(block => block.StartsInstruction)
                    .ToDictionary(block => block.EntryOrigin);
                BlockNode dispatch = EntryDispatch.Build(context, method, node => startBlockByNode[node]);
                HashSet<IGotoTarget> entryTargets = irMethod.Entries
                    .Select(entry => (IGotoTarget)entry.Block).ToHashSet();
                (BlockNode lowered, GotoCensus gotos, GotoTargets gotoTargets) = MethodBodyEmitter.Structure(
                    dispatch, loweredBlocks, loops, irMethod.Name, entryTargets, block => block.Origins,
                    allowLocalsDeclaredInBlock: true, localsDeclaredInDominatingBlocks: true);
                return (dispatch, lowered, gotos, gotoTargets);
            });
        timer.Measure("printing", () => {
            Dictionary<IrBlock, string> labels = BuildLabels(irMethod, method);
            writer.OpenBlock($"public virtual Action {method.MethodName}(int loadOffset)");
            CSharpStatementPrinter printer = new(writer, _expressions, target => target is IrBlock block
                ? labels[block]
                : throw new InvalidOperationException($"Method {method.MethodName}: goto target {target} is not an IR block."));
            foreach (VariableDeclarationNode variable in irMethod.MethodVariables) {
                printer.Print(variable);
            }
            if (structured.Targets.EntryDispatcher) {
                printer.Print(new EntryDispatcherLabelNode());
            }
            printer.Print(structured.EntryDispatch);
            if (!IsEmpty(structured.EntryDispatch)) {
                writer.Line();
            }
            printer.Print(structured.Body);
            writer.CloseBlock();
            writer.Line();
        });
        if (!collectCensuses) {
            return new MethodCensus(structured.Census, new FlagReadCensus(), new WebCensus(),
                liftedBlocks, liftedStatements);
        }
        FlagReadCensus flagReads = IrFlagReadCensusClassifier.Classify(irMethod, ssa, structured.Body);
        HashSet<ICfgNode> midBlockEntryNodes = irMethod.Entries
            .Where(entry => !entry.Node.Equals(entry.Block.EntryOrigin))
            .Select(entry => entry.Node).ToHashSet();
        WebCensus webCensus = IrWebCensusClassifier.Classify(ssa, pipeline.Webs!, midBlockEntryNodes);
        return new MethodCensus(structured.Census, flagReads, webCensus,
            liftedBlocks, liftedStatements);
    }

    /// <summary>
    /// Wraps the maximal run of consecutive fault-capable statements of each lowered boundary in a
    /// <see cref="CpuFaultTryCatchNode"/>, like <see cref="CpuFaultWrapper"/> does per instruction. A run
    /// never spans two lowered blocks. A fault-capable declaration is split: a bare
    /// <see cref="LocalDeclarationNode"/> prints before the run's <c>try</c> and the assignment stays
    /// inside, so a variable declared in one block stays in scope for a sibling block that reads it.
    /// </summary>
    private void WrapFaultRuns(List<LoweredBlock<IrBlock>> loweredBlocks, MethodPlan method) {
        for (int i = 0; i < loweredBlocks.Count; i++) {
            List<IVisitableAstNode> statements = [];
            foreach (IVisitableAstNode node in loweredBlocks[i].Body.Statements) {
                if (node is InstructionBoundaryNode boundary) {
                    statements.Add(boundary with { Body = WrapBoundary(boundary, method) });
                } else {
                    statements.Add(node);
                }
            }
            loweredBlocks[i] = loweredBlocks[i] with { Body = Sequence(statements) };
        }
    }

    private BlockNode WrapBoundary(InstructionBoundaryNode boundary, MethodPlan method) {
        List<IVisitableAstNode> statements = [];
        List<IVisitableAstNode> run = [];
        void FlushRun() {
            if (run.Count == 0) {
                return;
            }
            statements.Add(cpuFaultWrapper.Wrap((CfgInstruction)boundary.Node, Sequence(run), method));
            run = [];
        }
        foreach (IVisitableAstNode statement in boundary.Body.Statements) {
            if (!IrStatement.MayFault(statement, boundary.Node)) {
                FlushRun();
                statements.Add(statement);
                continue;
            }
            if (statement is VariableDeclarationNode declaration) {
                statements.Add(new LocalDeclarationNode(declaration.DataType, declaration.VariableName));
                run.Add(new BinaryOperationNode(declaration.DataType,
                    new VariableReferenceNode(declaration.DataType, declaration.VariableName),
                    BinaryOperation.ASSIGN, declaration.Initializer));
            } else {
                run.Add(statement);
            }
        }
        FlushRun();
        return Sequence(statements);
    }

    /// <summary>
    /// The label of every block: the method-plan label of its entry node when the block starts an
    /// instruction at a CFG block entry or method entry, otherwise <c>L_&lt;offset&gt;_&lt;n&gt;</c> with
    /// <c>n</c> the block's position among the blocks of its instruction.
    /// </summary>
    private static Dictionary<IrBlock, string> BuildLabels(IrMethod irMethod, MethodPlan method) {
        HashSet<ICfgNode> planLabelled = method.Blocks.Select(block => block.Entry)
            .Concat(method.Entries.Select(entry => entry.Node)).ToHashSet();
        HashSet<string> taken = planLabelled.Select(method.GetLabel).ToHashSet();
        Dictionary<ICfgNode, int> positionByOrigin = [];
        Dictionary<IrBlock, string> labels = [];
        foreach (IrBlock block in irMethod.Blocks) {
            ICfgNode origin = block.EntryOrigin;
            positionByOrigin.TryGetValue(origin, out int position);
            positionByOrigin[origin] = position + 1;
            if (block.StartsInstruction && planLabelled.Contains(origin)) {
                labels[block] = method.GetLabel(origin);
                continue;
            }
            labels[block] = UniqueLabel($"L_{origin.Address.Offset:X4}_{position}", taken);
        }
        return labels;
    }

    /// <summary>
    /// The first free variant of <paramref name="label"/>: itself when free, else
    /// <c>label_ir</c>, <c>label_ir2</c>, <c>label_ir3</c> and so on. The result is added to
    /// <paramref name="taken"/>.
    /// </summary>
    internal static string UniqueLabel(string label, HashSet<string> taken) {
        if (taken.Add(label)) {
            return label;
        }
        string candidate = $"{label}_ir";
        for (int suffix = 2; !taken.Add(candidate); suffix++) {
            candidate = $"{label}_ir{suffix}";
        }
        return candidate;
    }
}
