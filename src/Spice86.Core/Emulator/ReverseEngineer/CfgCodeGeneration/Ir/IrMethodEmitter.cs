namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Shared.Emulator.Memory;

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
    HardwareInterruptEffectBuilder effectBuilder,
    bool collectCensuses,
    bool verify) {

    private readonly IrLifter _lifter = new(context, lowerer, assemblyRenderer);

    /// <summary>Builds the AST of <paramref name="method"/> and times the lift, each IR pass and the
    /// lowering and structuring into <paramref name="timer"/>. Printing is deferred to
    /// <see cref="Print"/> so the generator can resolve global names over the accesses of the whole
    /// program before any method is printed.</summary>
    /// <returns>The prepared method to hand to <see cref="Print"/>.</returns>
    public PreparedMethod Prepare(MethodPlan method, GenerationStepTimer timer) {
        IrMethod irMethod = timer.Measure("lift", () => _lifter.Lift(method));
        int liftedBlocks = irMethod.Blocks.Count;
        int liftedStatements = irMethod.Blocks.Sum(block => block.Statements.Count);
        IrFullPipelineResult pipeline = IrPassPipeline.RunAll(irMethod, verify, collectCensuses,
            context.Program.Inputs.IoPorts, timer);
        ConstantMemoryAccessTable constantMemoryAccesses = IrConstantMemoryAccesses.Collect(irMethod);
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
                BlockNode dispatch = EntryDispatch.Build(context, method, lowerer.HardwareInterruptEffect, node => startBlockByNode[node]);
                HashSet<IGotoTarget> entryTargets = irMethod.Entries
                    .Select(entry => (IGotoTarget)entry.Block).ToHashSet();
                (BlockNode lowered, GotoCensus gotos, GotoTargets gotoTargets) = MethodBodyEmitter.Structure(
                    dispatch, loweredBlocks, loops, irMethod.Name, entryTargets, block => block.Origins,
                    allowLocalsDeclaredInBlock: true, localsDeclaredInDominatingBlocks: true);
                return (dispatch, lowered, gotos, gotoTargets);
            });
        FrameSlotPlan? framePlan = timer.Measure("frame slots", () => BuildFramePlan(irMethod, pipeline.Ssa, method));
        SaveRestorePairs.SavedPairPlan savedPairs = SaveRestorePairs.Plan(irMethod, pipeline.Ssa);
        BlockNode body = structured.Body;
        if (savedPairs.Pairs.Count > 0) {
            body = SaveRestorePairs.Apply(body, savedPairs);
        }
        MethodEmission emission = BuildEmission(irMethod, pipeline, structured.Census,
            body, liftedBlocks, liftedStatements, constantMemoryAccesses, framePlan);
        return new PreparedMethod(method, irMethod, pipeline.Ssa, pipeline.DosCalls,
            structured.EntryDispatch, body,
            structured.Targets, emission, framePlan, savedPairs);
    }

    /// <summary>The frame slot plan of <paramref name="irMethod"/> when it accesses SS through a
    /// register term: the frame analysis runs only then (every other method has nothing to prove).
    /// </summary>
    private FrameSlotPlan? BuildFramePlan(IrMethod irMethod, IrSsa ssa, MethodPlan method) {
        bool stackAccess = irMethod.Blocks.SelectMany(block => block.Statements).Any(statement =>
            IrAstChildren.Descendants(statement.Node).OfType<SegmentedPointerNode>()
                .Any(pointer => pointer.Segment is SegmentRegisterNode {
                    RegisterIndex: (int)SegmentRegisterIndex.SsIndex }));
        if (!stackAccess) {
            return null;
        }
        IrFrames frames = IrFrames.Compute(irMethod, ssa,
            IrStackSlots.Compute(irMethod, new IrMemoryAliasAnalysis(ssa)),
            statement => {
                if (statement.Origin is CfgInstruction instruction) {
                    return effectBuilder.CallTargetWrites(instruction);
                }
                return CpuValues.Registers;
            });
        return FrameSlotPlan.Build(irMethod, frames, $"{method.MethodName}Frame");
    }

    /// <summary>Resolves the per-method print inputs of <paramref name="prepared"/> from
    /// <paramref name="analysis"/>: its typed signature, its register-local plan with the
    /// write-backs applied to the body (the plan is dropped and the original body kept when the
    /// write-backs cannot be applied) and its parameter reads. Timed into
    /// <paramref name="timer"/>.</summary>
    public AnalyzedMethod Analyze(PreparedMethod prepared, ProgramAnalysis analysis, GenerationStepTimer timer) {
        MethodPlan method = prepared.Method;
        return timer.Measure("register locals", () => {
            TypedSignature? signature = analysis.TypedSignatures.Of(method.MethodName);
            RegisterLocalPlan? registerLocals = RegisterLocalPromotion.Plan(prepared.IrMethod, prepared.Ssa,
                ReservedLocalNames(prepared.IrMethod, signature, prepared.FramePlan),
                UnprintedNodes(prepared.FramePlan),
                (statement, mask) => CallPreserves(analysis.CallingConventions, method.MethodName,
                    statement, mask));
            BlockNode body = prepared.Body;
            if (registerLocals.WriteBacks.Count > 0 || registerLocals.Dropped.Count > 0) {
                if (RegisterLocalPromotion.ApplyToBody(prepared.Body, registerLocals)
                        is { } rewrittenBody) {
                    body = rewrittenBody;
                } else {
                    registerLocals = null;
                }
            }
            IReadOnlyDictionary<IVisitableAstNode, string>? parameterReads = null;
            if (signature is not null) {
                parameterReads = analysis.TypedSignatures.BuildParameterReads(prepared.IrMethod,
                    prepared.Ssa, signature);
            }
            return new AnalyzedMethod(prepared, signature, parameterReads, registerLocals, body);
        });
    }

    /// <summary>The node instances the frame plan replaces when it prints: every descendant of its
    /// pointer nodes, which print as frame slots instead of memory accesses.</summary>
    private static IReadOnlySet<IVisitableAstNode> UnprintedNodes(FrameSlotPlan? framePlan) {
        HashSet<IVisitableAstNode> unprinted = new(ReferenceEqualityComparer.Instance);
        if (framePlan is null) {
            return unprinted;
        }
        foreach (SegmentedPointerNode pointer in framePlan.Pointers) {
            unprinted.Add(pointer);
            unprinted.UnionWith(IrAstChildren.Descendants(pointer));
        }
        return unprinted;
    }

    /// <summary>Prints an analyzed method as one C# method to <paramref name="writer"/>, timed into
    /// <paramref name="timer"/>.</summary>
    public void Print(CSharpSourceWriter writer, AnalyzedMethod method, ProgramAnalysis analysis,
        GenerationStepTimer timer) {
        timer.Measure("printing", () => {
            PrintHeader(writer, method.Prepared.Method, analysis);
            writer.OpenBlock(SignatureText(method.Prepared.Method, method.Signature));
            PrintBody(writer, method, analysis);
            PrintTail(writer, method.Prepared.Method, method.Signature, method.Prepared.FramePlan);
        });
    }

    /// <summary>The <c>public virtual</c> line that opens the method: typed signature, entry
    /// dispatch, or parameterless.</summary>
    private static string SignatureText(MethodPlan method, TypedSignature? signature) {
        if (signature is not null) {
            return $"public virtual {signature.ReturnType} {method.MethodName}({signature.ParameterList})";
        }
        if (method.NeedsEntryDispatch) {
            return $"public virtual Action {method.MethodName}(int loadOffset)";
        }
        return $"public virtual Action {method.MethodName}()";
    }

    /// <summary>The doc comments printed above the signature: conventions and segment values at
    /// each entry.</summary>
    private void PrintHeader(CSharpSourceWriter writer, MethodPlan method, ProgramAnalysis analysis) {
        PrintConventionSummary(writer, method, analysis.CallingConventions);
        foreach (string comment in EntryValueComments(method, analysis.SegmentSummaries)) {
            writer.Line(comment);
        }
    }

    /// <summary>The parameter-to-register assignments, declarations, entry dispatch and structured
    /// body inside the method's block, followed by the closing brace.</summary>
    private void PrintBody(CSharpSourceWriter writer, AnalyzedMethod analyzed,
        ProgramAnalysis analysis) {
        PreparedMethod prepared = analyzed.Prepared;
        MethodPlan method = prepared.Method;
        IrMethod irMethod = prepared.IrMethod;
        Dictionary<IrBlock, string> labels = BuildLabels(irMethod, method);
        string LabelOf(IGotoTarget target) {
            if (target is IrBlock block) {
                return labels[block];
            }
            throw new InvalidOperationException(
                $"Method {method.MethodName}: goto target {target} is not an IR block.");
        }
        CSharpExpressionPrinter expressions = new(context.SegmentVariables,
            context.Program.Inputs.IoPorts, analyzed, analysis);
        CSharpStatementPrinter printer = new(writer, expressions, LabelOf);
        // Every read the body makes of a register the signature declares must see the argument:
        // the assignment sits before the entrydispatcher label so a cyclic jump to an entry
        // does not reassign stale arguments.
        if (analyzed.Signature is { } signature) {
            foreach (TypedParameter parameter in signature.Parameters) {
                writer.Line($"{parameter.Register} = {parameter.Name};");
            }
        }
        if (analyzed.RegisterLocals is { } promoted) {
            foreach (LocalDeclarationNode declaration in promoted.Declarations) {
                printer.Print(declaration);
            }
        }
        foreach (LocalDeclarationNode declaration in SaveRestorePairs.Declarations(prepared.SavedPairs)) {
            printer.Print(declaration);
        }
        foreach (VariableDeclarationNode variable in irMethod.MethodVariables) {
            printer.Print(variable);
        }
        if (prepared.Targets.EntryDispatcher) {
            printer.Print(new EntryDispatcherLabelNode());
        }
        if (prepared.FramePlan is { } framePlan) {
            // The frame is declared after the entry dispatcher label, so a cyclic jump to an
            // entry re-creates it with that entry's SP.
            writer.Line($"{framePlan.ClassName} {framePlan.VariableName} = new(Memory, "
                + "State.SegmentRegisters, SP);");
        }
        printer.Print(prepared.EntryDispatch);
        if (!IsEmpty(prepared.EntryDispatch)) {
            writer.Line();
        }
        printer.Print(analyzed.Body);
        writer.CloseBlock();
    }

    /// <summary>What follows the method's closing brace: the <c>_Asm</c> wrapper of a typed method,
    /// the frame class, and the trailing blank line.</summary>
    private static void PrintTail(CSharpSourceWriter writer, MethodPlan method,
        TypedSignature? signature, FrameSlotPlan? framePlan) {
        if (signature is not null) {
            PrintAsmWrapper(writer, method, signature);
        }
        if (framePlan is not null) {
            writer.Line();
            PrintFrameClass(writer, framePlan);
        }
        writer.Line();
    }

    /// <summary>Whether <paramref name="statement"/> is a call that provably returns without
    /// changing a byte of <paramref name="mask"/>: a resolved call site with no redirected-return
    /// continuation whose every target returns keeping the bytes.</summary>
    private bool CallPreserves(IrCallingConventions conventions, string methodName,
        IrStatement statement, CpuValues mask) {
        if (statement.Origin is not CfgInstruction { IsCall: true } instruction
            || context.ResolveCallContinuation(instruction).RedirectedContinuationEdges.Count != 0) {
            return false;
        }
        return conventions.CallPreserves(methodName, statement, mask);
    }

    /// <summary>Every local name a register local may not take in <paramref name="irMethod"/>: the
    /// typed signature's parameters, the frame variable, the method variables and every temporary or
    /// declared name appearing in the method.</summary>
    private static HashSet<string> ReservedLocalNames(IrMethod irMethod, TypedSignature? signature,
        FrameSlotPlan? framePlan) {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (VariableDeclarationNode variable in irMethod.MethodVariables) {
            names.Add(variable.VariableName);
        }
        foreach (IrBlock block in irMethod.Blocks) {
            foreach (IVisitableAstNode node in block.Statements
                    .SelectMany(statement => IrAstChildren.Descendants(statement.Node))
                    .Concat(IrAstChildren.TerminatorNodes(block.Terminator))) {
                switch (node) {
                    case VariableDeclarationNode declaration:
                        names.Add(declaration.VariableName);
                        break;
                    case VariableReferenceNode reference:
                        names.Add(reference.VariableName);
                        break;
                    case LocalDeclarationNode local:
                        names.Add(local.VariableName);
                        break;
                }
            }
        }
        if (signature is not null) {
            foreach (TypedParameter parameter in signature.Parameters) {
                names.Add(parameter.Name);
            }
        }
        if (framePlan is not null) {
            names.Add(framePlan.VariableName);
        }
        return names;
    }

    /// <summary>One doc comment line per partition entry of <paramref name="method"/> whose
    /// segment summary proves a value or an equality at it, like
    /// <c>/// Segment values at entry, from every call site: DS = 0x2000, ES = DS.</c>.</summary>
    private IEnumerable<string> EntryValueComments(MethodPlan method,
        IrSegmentSummaries summaries) {
        foreach (CfgCodePartitionEntry entry in context.GetEntries(method.Partition)) {
            List<string> parts = [];
            HashSet<IrSsaValue> knownRegisters = [];
            foreach (IrSsaValue register in IrSegmentSummaries.SegmentRegisters
                    .Where(register => summaries.EntryValue(entry, register)
                        is SegmentValue.Known)) {
                SegmentValue.Known known =
                    (SegmentValue.Known)summaries.EntryValue(entry, register);
                parts.Add($"{RegisterName(register)} = 0x{known.Value:X4}");
                knownRegisters.Add(register);
            }
            foreach (IReadOnlySet<IrSsaValue> equality in summaries.EqualityClasses(entry)) {
                IrSsaValue first = IrSegmentSummaries.SegmentRegisters.First(equality.Contains);
                foreach (IrSsaValue member in IrSegmentSummaries.SegmentRegisters
                             .Where(equality.Contains).Skip(1)
                             .Where(member => !knownRegisters.Contains(member))) {
                    parts.Add($"{RegisterName(member)} = {RegisterName(first)}");
                }
            }
            if (parts.Count > 0) {
                yield return $"/// Segment values at entry, from every call site: {string.Join(", ", parts)}.";
            }
            if (summaries.EntryValue(entry, IrSsaValue.Direction) is SegmentValue.Known direction) {
                yield return $"/// Direction flag at entry, from every call site: {(direction.Value != 0 ? "set" : "clear")}.";
            }
        }
    }

    private static string RegisterName(IrSsaValue value) => value.ToString().ToUpperInvariant();

    /// <summary>Writes the <c>Action</c> entry wrapper of a typed method right after it: it calls
    /// the typed method with the register arguments, stores the outputs back and returns
    /// <c>Returned</c>. Every delegate reference and partition return uses this name.</summary>
    private static void PrintAsmWrapper(CSharpSourceWriter writer, MethodPlan method, TypedSignature signature) {
        writer.Line();
        writer.OpenBlock($"public virtual Action {method.MethodName}_Asm()");
        string invocation = $"{method.MethodName}({signature.ArgumentList});";
        writer.Line(signature.Outputs.Count switch {
            0 => invocation,
            1 => $"{signature.Outputs[0].Register} = {invocation}",
            _ => $"({string.Join(", ", signature.Outputs.Select(output => output.Register))}) = {invocation}"
        });
        writer.Line("return Returned;");
        writer.CloseBlock();
    }

    /// <summary>Writes the frame struct of <paramref name="plan"/> after its method: one property
    /// per slot, forwarding to the <see cref="GeneratedStackFrame"/> accessors at its entry offset.
    /// A struct so a generated method allocates nothing for its frame.</summary>
    private static void PrintFrameClass(CSharpSourceWriter writer, FrameSlotPlan plan) {
        writer.OpenBlock($"public readonly struct {plan.ClassName}(IMemory memory, "
            + "SegmentRegisters segmentRegisters, ushort entrySp)");
        writer.Line("private readonly GeneratedStackFrame _slots = new(memory, segmentRegisters, entrySp);");
        foreach (FrameSlotPlan.Property property in plan.Properties) {
            (string accessor, string type, string widthName) = property.Width switch {
                1 => ("Byte", "byte", "Byte"),
                4 => ("Dword", "uint", "Double word"),
                _ => ("Word", "ushort", "Word")
            };
            string offset = OffsetLiteral(property.EntryOffset);
            writer.Line();
            writer.Line(PropertyDoc(property.EntryOffset, widthName));
            writer.Line($"public {type} {property.Name} {{ get => _slots.Get{accessor}({offset}); "
                + $"set => _slots.Set{accessor}({offset}, value); }}");
        }
        writer.CloseBlock();
    }

    private static string PropertyDoc(int entryOffset, string widthName) {
        if (entryOffset < 0) {
            return $"/// <summary>{widthName} at SS:[entry SP - 0x{-entryOffset:X4}].</summary>";
        }
        return $"/// <summary>{widthName} at SS:[entry SP + 0x{entryOffset:X4}].</summary>";
    }

    private static string OffsetLiteral(int entryOffset) =>
        entryOffset < 0 ? $"-0x{-entryOffset:X4}" : $"0x{entryOffset:X4}";

    /// <summary>The doc comment carrying the entry's register inputs, outputs and changes: one
    /// <c>&lt;summary&gt;</c> line for a single-entry method, one line per entry for a multi-entry
    /// method.</summary>
    private void PrintConventionSummary(CSharpSourceWriter writer, MethodPlan method,
        IrCallingConventions conventions) {
        if (method.Entries.Count == 1) {
            writer.Line(ConventionSummary(conventions, method.Entries[0]));
            return;
        }
        writer.Line("/// <summary>");
        foreach (CfgCodePartitionEntry entry in method.Entries) {
            writer.Line(EntryConventionSummary(conventions, entry));
        }
        writer.Line("/// </summary>");
    }

    private static string ConventionSummary(IrCallingConventions conventions,
        CfgCodePartitionEntry entry) {
        string inputs = IrCallingConventions.Describe(conventions.EffectOf(entry).Reads);
        if (!conventions.Returns(entry)) {
            return $"/// <summary>Inputs: {inputs}. Does not return.</summary>";
        }
        return $"/// <summary>Inputs: {inputs}. "
            + $"Outputs: {IrCallingConventions.Describe(conventions.OutputsOf(entry))}. "
            + $"Changes: {IrCallingConventions.Describe(conventions.EffectOf(entry).Writes)}.</summary>";
    }

    private static string EntryConventionSummary(IrCallingConventions conventions,
        CfgCodePartitionEntry entry) {
        string inputs = IrCallingConventions.Describe(conventions.EffectOf(entry).Reads);
        if (!conventions.Returns(entry)) {
            return $"/// Entry {entry.Address}: inputs {inputs}; does not return.";
        }
        return $"/// Entry {entry.Address}: inputs {inputs}; outputs "
            + $"{IrCallingConventions.Describe(conventions.OutputsOf(entry))}; changes "
            + $"{IrCallingConventions.Describe(conventions.EffectOf(entry).Writes)}.";
    }

    private MethodEmission BuildEmission(IrMethod irMethod, IrFullPipelineResult pipeline,
        GotoCensus gotos, BlockNode body, int liftedBlocks, int liftedStatements,
        ConstantMemoryAccessTable constantMemoryAccesses, FrameSlotPlan? framePlan) {
        if (!collectCensuses) {
            return new MethodEmission(new MethodCensus(gotos, new FlagReadCensus(),
                new WebCensus(), liftedBlocks, liftedStatements), constantMemoryAccesses);
        }
        IrSsa ssa = pipeline.Ssa;
        FlagReadCensus flagReads = IrFlagReadCensusClassifier.Classify(irMethod, ssa, body);
        HashSet<ICfgNode> midBlockEntryNodes = irMethod.Entries
            .Where(entry => !entry.Node.Equals(entry.Block.EntryOrigin))
            .Select(entry => entry.Node).ToHashSet();
        WebCensus webCensus = IrWebCensusClassifier.Classify(ssa, pipeline.Webs!, midBlockEntryNodes);
        return new MethodEmission(new MethodCensus(gotos, flagReads, webCensus,
            liftedBlocks, liftedStatements), constantMemoryAccesses);
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
                statements.Add(new LocalDeclarationNode(declaration.DataType,
                    declaration.VariableName, null));
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
