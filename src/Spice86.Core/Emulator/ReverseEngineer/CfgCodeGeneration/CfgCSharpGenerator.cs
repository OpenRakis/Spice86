namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Microsoft.Extensions.Logging.Abstractions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Naming;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// The top-level orchestrator: takes a partitioned CFG program and produces a complete, compilable C# source
/// file. Runs the pipeline in order: analyze → plan → lower each method → render to text.
/// </summary>
internal sealed class CfgCSharpGenerator {
    private const string EntrySuffix = "_entry";
    private const long SlowMethodThresholdMilliseconds = 1000;

    /// <summary>Maximum printed lines of one generated file; a segment file over the limit is split at
    /// method boundaries (a method is never split).</summary>
    public const int MaxLinesPerFile = 4000;

    private readonly AstInstructionRenderer _assemblyRenderer = new(AsmRenderingConfig.CreateSpice86Style());
    private readonly bool _collectCensuses;
    private readonly bool _verify;
    private readonly int _maxLinesPerFile;
    private readonly bool _printAsmComments;
    private readonly GenerationPolicy _policy;

    /// <summary>The production generator: no census collection, no IR verification, no ASM
    /// instruction comments.</summary>
    public CfgCSharpGenerator() : this(false, false, false) {
    }

    /// <summary>Creates a generator; production uses the parameterless options.</summary>
    /// <param name="collectCensuses">Whether each emitted method runs the census classifiers the
    /// generated-program diagnostics read; production skips them.</param>
    /// <param name="verify">Whether the IR invariants are checked after the passes; tests enable it.
    /// </param>
    public CfgCSharpGenerator(bool collectCensuses, bool verify)
        : this(collectCensuses, verify, false, MaxLinesPerFile) {
    }

    /// <summary>Creates a generator with the ASM comment option.</summary>
    /// <param name="printAsmComments">Whether every instruction keeps its
    /// <c>// SSSS:OOOO asm</c> comment; the address map records the address either way.</param>
    internal CfgCSharpGenerator(bool collectCensuses, bool verify, bool printAsmComments)
        : this(collectCensuses, verify, printAsmComments, MaxLinesPerFile) {
    }

    /// <summary>Creates a generator with a per-file line limit; tests use a small limit.</summary>
    internal CfgCSharpGenerator(bool collectCensuses, bool verify, int maxLinesPerFile)
        : this(collectCensuses, verify, false, maxLinesPerFile) {
    }

    /// <summary>Creates a generator with the ASM comment option and an explicit policy.</summary>
    internal CfgCSharpGenerator(bool collectCensuses, bool verify, bool printAsmComments,
        GenerationPolicy policy) : this(collectCensuses, verify, printAsmComments, policy,
        MaxLinesPerFile) {
    }

    /// <summary>Creates a generator with a per-file line limit and explicit comment option.</summary>
    internal CfgCSharpGenerator(bool collectCensuses, bool verify, bool printAsmComments,
        int maxLinesPerFile) : this(collectCensuses, verify, printAsmComments, GenerationPolicy.Strict,
        maxLinesPerFile) {
    }

    /// <summary>Creates a generator with a per-file line limit, explicit comment option and
    /// explicit policy.</summary>
    internal CfgCSharpGenerator(bool collectCensuses, bool verify, bool printAsmComments,
        GenerationPolicy policy, int maxLinesPerFile) {
        _collectCensuses = collectCensuses;
        _verify = verify;
        _printAsmComments = printAsmComments;
        _maxLinesPerFile = maxLinesPerFile;
        _policy = policy;
    }

    /// <summary>Generates without progress reporting; the overload used by tests and tools.</summary>
    public GeneratedCSharpProgram Generate(CfgPartitionedProgram program) =>
        Generate(program, new DumpProgressReporter(NullLogger.Instance, new SystemTimeProvider()));

    /// <summary>
    /// Generates the override source for <paramref name="program"/>, reporting the analysis, planning
    /// and per-method emission phases through <paramref name="progress"/>.
    /// </summary>
    public GeneratedCSharpProgram Generate(CfgPartitionedProgram program, DumpProgressReporter progress) {
        if (program.Partitions.Count == 0) {
            throw new InvalidOperationException("Cannot generate C# for an empty CFG partitioned program.");
        }
        progress.BeginPhase(DumpPhase.CodeAnalysis);
        InstructionMnemonic instructionMnemonic = new(_assemblyRenderer);
        GeneratorAnalysis generatorAnalysis = GeneratorAnalysis.Build(program, instructionMnemonic,
            _policy);
        progress.EndPhase();

        progress.BeginPhase(DumpPhase.CodePlan);
        CfgGeneratorContext context = generatorAnalysis.Context;
        GenerationPlan plan = GenerationPlanBuilder.Build(context);
        TransferEmitter transferEmitter = new(context);
        HardwareInterruptEffectBuilder effectBuilder =
            new(context, transferEmitter, _assemblyRenderer, plan);
        InstructionLowerer lowerer = new(context, transferEmitter, effectBuilder,
            effectBuilder.Compute());
        CpuFaultWrapper cpuFaultWrapper = new(context, transferEmitter);
        IrMethodEmitter methodEmitter = new(context, cpuFaultWrapper, lowerer, _assemblyRenderer,
            effectBuilder, _collectCensuses, _verify, _printAsmComments);

        List<GeneratedSourceFile> files = new(2 + plan.Methods.Count);

        CSharpSourceWriter supplierWriter = new();
        EmitFilePreamble(supplierWriter, false);
        supplierWriter.Line(_policy.HeaderLine);
        supplierWriter.Line();
        EmitSupplier(supplierWriter, plan);
        progress.EndPhase();

        progress.BeginPhase(DumpPhase.CodeMethods);
        GotoCensus census = new();
        FlagReadCensus flagReadCensus = new();
        WebCensus webCensus = new();
        ConstantMemoryAccessTable constantMemoryAccesses = new();
        List<PreparedMethod> prepared = new(plan.Methods.Count);
        List<GenerationStepTimer> timers = new(plan.Methods.Count);
        for (int i = 0; i < plan.Methods.Count; i++) {
            MethodPlan method = plan.Methods[i];
            progress.Step(i + 1, plan.Methods.Count, method.MethodName);
            GenerationStepTimer timer = new();
            PreparedMethod preparedMethod = methodEmitter.Prepare(method, timer);
            prepared.Add(preparedMethod);
            timers.Add(timer);
            MethodCensus methodCensus = preparedMethod.Emission.Census;
            constantMemoryAccesses.Merge(preparedMethod.Emission.ConstantMemoryAccesses);
            flagReadCensus.Merge(methodCensus.FlagReads);
            webCensus.Merge(methodCensus.Webs);
        }
        progress.EndPhase();

        progress.BeginPhase(DumpPhase.CodeProgramAnalysis);
        ProgramAnalysis analysis = ProgramAnalysis.Compute(prepared, context, effectBuilder,
            program.Inputs.ProgramImages, progress);

        // A return exit reads only the outputs of its method's entries (plus every value the
        // convention model does not describe and both tokens), and a resolved call reads and may
        // write only what its target's convention and the hardware contract describe: methods where
        // either set is smaller than a first-round universal effect are prepared a second time with
        // them, so the pipeline sees the real demand from the start.
        int reprepared = 0;
        Dictionary<IrCallingConventions.CallSummarySkip, int> callSkips = new();
        int summarizedCalls = 0;
        UsedFacts?[] used = new UsedFacts?[prepared.Count];
        List<IReadOnlyDictionary<ICfgNode, IrEffects>> callEffectsByMethod = new(prepared.Count);
        for (int i = 0; i < prepared.Count; i++) {
            IReadOnlyDictionary<ICfgNode, CpuValues> exitReads =
                ExitReadsOf(prepared[i], analysis.ObservedConventions);
            IReadOnlyDictionary<ICfgNode, IrEffects> exitEffects = ExitEffectsFromReads(exitReads);
            IReadOnlyDictionary<ICfgNode, IrEffects> callEffects =
                CallEffectsOf(prepared[i], analysis.ObservedConventions,
                    lowerer.HardwareInterruptEffect, callSkips);
            callEffectsByMethod.Add(callEffects);
            summarizedCalls += callEffects.Count;
            IReadOnlyDictionary<ICfgNode, CpuValues> faultReads =
                FaultReadsOf(prepared[i], analysis.ObservedConventions, context);
            IReadOnlyDictionary<ICfgNode, IrSegmentEntryFact> entryFacts =
                EntryFactsOf(prepared[i], analysis.SegmentSummaries);
            if (exitEffects.Count == 0 && callEffects.Count == 0 && faultReads.Count == 0
                && entryFacts.Count == 0) {
                continue;
            }
            UsedFacts facts = new();
            foreach ((ICfgNode origin, CpuValues reads) in exitReads) {
                facts.ExitReads[origin] = reads;
            }
            foreach ((ICfgNode origin, IrEffects summary) in callEffects) {
                facts.CallSummaries[origin] = summary;
                facts.CallReads[origin] = summary.Reads;
                facts.CallMayWrite[origin] = summary.MayWrite;
            }
            foreach ((ICfgNode origin, CpuValues reads) in faultReads) {
                facts.FaultReads[origin] = reads;
            }
            foreach ((ICfgNode entry, IrSegmentEntryFact fact) in entryFacts) {
                facts.EntryFacts[entry] = fact;
            }
            used[i] = facts;
            reprepared++;
            GenerationStepTimer timer = timers[i];
            MethodPlan methodPlan = plan.Methods[i];
            prepared[i] = timer.Measure("round 2 prepare",
                () => methodEmitter.Prepare(methodPlan, timer, exitEffects, callEffects,
                    faultReads, entryFacts));
        }
        progress.Detail($"Resolved calls summarized: {summarizedCalls}; kept Unknown: "
            + string.Join(", ", callSkips
                .OrderBy(pair => pair.Key.ToString())
                .Select(pair => $"{pair.Key}={pair.Value}")));
        int rounds = 1;
        if (reprepared > 0) {
            // The conventions over the re-prepared methods can demand a byte a used set did not
            // cover: a demand that was killed by a universal call effect now passes through the
            // summary. The soundness condition is on the consumed facts, not on printed sets:
            // every exit and summarized call must read and may write a superset of what the final
            // conventions demand of it. A method that violates it is prepared again with the
            // union of what every round demanded; the unions only grow over a finite lattice, so
            // this terminates - the round cap is a bug detector, not a tolerance.
            progress.Detail("Preparation round 2");
            ProgramAnalysis current = ProgramAnalysis.Compute(prepared, context, effectBuilder,
                program.Inputs.ProgramImages, progress);
            rounds = 2;
            while (true) {
                List<(int Index, string Reason)> violated = ConsumeViolations(prepared, used,
                    current.ObservedConventions, current.SegmentSummaries,
                    lowerer.HardwareInterruptEffect, context);
                if (violated.Count == 0) {
                    analysis = current;
                    break;
                }
                if (rounds >= 8) {
                    throw new InvalidOperationException(
                        $"Round-two conventions did not converge in {rounds} rounds: "
                        + string.Join("; ", violated.Select(violation =>
                            $"{prepared[violation.Index].Method.MethodName}: {violation.Reason}")));
                }
                rounds++;
                foreach ((int index, _) in violated) {
                    UsedFacts facts = used[index]!;
                    IReadOnlyDictionary<ICfgNode, IrEffects> exitEffects =
                        ExitEffectsFromReads(facts.ExitReads);
                    IReadOnlyDictionary<ICfgNode, IrEffects> callEffects =
                        CallEffectsFromUsed(facts);
                    callEffectsByMethod[index] = callEffects;
                    GenerationStepTimer timer = timers[index];
                    prepared[index] = timer.Measure($"round {rounds} prepare",
                        () => methodEmitter.Prepare(plan.Methods[index], timer,
                            exitEffects, callEffects, facts.FaultReads, facts.EntryFacts));
                }
                progress.Detail($"Preparation round {rounds}");
                current = ProgramAnalysis.Compute(prepared, context, effectBuilder,
                    program.Inputs.ProgramImages, progress);
            }
            if (_verify) {
                VerifyObservedExitReads(prepared, analysis.ObservedConventions);
                VerifyObservedCallReads(prepared, callEffectsByMethod, analysis.ObservedConventions);
                VerifyObservedFaultReads(prepared, used, analysis.ObservedConventions, context);
            }
            progress.Detail($"Round-two conventions computed over {reprepared} re-prepared methods in {rounds} rounds");
        }
        RewritePreparedBodies(prepared, program, analysis);

        IReadOnlyList<SpeculativeRunFieldPlan> speculativeRunFields = NameSpeculativeRunFields(prepared);
        progress.EndPhase();

        progress.BeginPhase(DumpPhase.CodePrinting);
        List<(ushort Segment, string Text, bool HasFrame, bool Support, uint EntryLinear,
                string MethodName, IReadOnlyList<MethodInstructionLine> Instructions)> methodTexts =
            new(plan.Methods.Count);
        for (int i = 0; i < prepared.Count; i++) {
            PreparedMethod preparedMethod = prepared[i];
            progress.Step(i + 1, prepared.Count, preparedMethod.Method.MethodName);
            GenerationStepTimer timer = timers[i];
            CSharpSourceWriter methodWriter = new();
            methodWriter.Indent();
            AnalyzedMethod analyzed = methodEmitter.Analyze(preparedMethod, analysis, timer);
            List<MethodInstructionLine> instructionLines =
                methodEmitter.Print(methodWriter, analyzed, analysis, timer);
            bool support = program.Inputs.ProgramImages.IsSupportCode(preparedMethod.Method.Nodes
                .OfType<CfgInstruction>()
                .Select(instruction => instruction.Address.Linear)
                .ToList());
            methodTexts.Add((preparedMethod.Method.PrimaryEntry.Address.Segment, methodWriter.ToString(),
                preparedMethod.FramePlan is not null, support,
                preparedMethod.Method.PrimaryEntry.Address.Linear,
                preparedMethod.Method.MethodName, instructionLines));
            MethodCensus methodCensus = preparedMethod.Emission.Census;
            census.Merge(methodCensus.Gotos);
            if (timer.TotalMilliseconds >= SlowMethodThresholdMilliseconds) {
                MethodPlan method = preparedMethod.Method;
                progress.Detail(SlowMethodLine(i + 1, prepared.Count, method.MethodName,
                    method.Nodes.OfType<CfgInstruction>().Count(),
                    methodCensus.LiftedBlocks, methodCensus.LiftedStatements, timer));
            }
        }
        List<GeneratedAddressMapEntry> addressMap = new(plan.Methods
            .Sum(method => method.Nodes.OfType<CfgInstruction>().Count()));
        files.AddRange(BuildSegmentFiles(methodTexts
            .Where(method => !method.Support)
            .Select(method => (method.Segment, method.Text, method.HasFrame, method.MethodName,
                method.Instructions))
            .ToList(), addressMap));
        files.AddRange(EmitGlobalsFiles(analysis.GlobalClasses));
        files.AddRange(EmitTableFiles(analysis.GlobalClasses));

        CSharpSourceWriter wiringWriter = new();
        EmitFilePreamble(wiringWriter, false);
        wiringWriter.OpenBlock($"public partial class {GeneratedOverrideNames.OverrideClassName} : CSharpOverrideHelper");
        EmitSegmentFields(wiringWriter, plan);
        EmitEntryFields(wiringWriter, plan, analysis.TypedSignatures);
        EmitGlobalsFields(wiringWriter, analysis.GlobalClasses.Classes, analysis.GlobalClasses.TableClasses);
        EmitConstructor(wiringWriter, plan, analysis.GlobalClasses.Classes, analysis.GlobalClasses.TableClasses,
            analysis.TypedSignatures);
        EmitHardwareInterruptDispatch(wiringWriter, program, plan, analysis.TypedSignatures);
        wiringWriter.Line("protected override bool ObservedCallLandingsOnly => true;");
        wiringWriter.CloseBlock();
        files.Add(new GeneratedSourceFile(GeneratedOverrideNames.SupportWiringFileName, wiringWriter.ToString()));

        CSharpSourceWriter guardsWriter = new();
        EmitFilePreamble(guardsWriter, false);
        guardsWriter.OpenBlock($"public partial class {GeneratedOverrideNames.OverrideClassName} : CSharpOverrideHelper");
        EmitSignatureFields(guardsWriter, plan.SignatureFields);
        EmitSpeculativeRunFields(guardsWriter, speculativeRunFields);
        guardsWriter.CloseBlock();
        files.Add(new GeneratedSourceFile(GeneratedOverrideNames.SupportGuardsFileName, guardsWriter.ToString()));

        files.AddRange(BuildSupportStubFiles(methodTexts
            .Where(method => method.Support)
            .Select(method => (method.Text, method.HasFrame, method.EntryLinear, method.MethodName,
                method.Instructions))
            .ToList(), addressMap));
        files.Add(new GeneratedSourceFile(GeneratedOverrideNames.SupplierFileName, supplierWriter.ToString()));
        progress.EndPhase();

        return new GeneratedCSharpProgram {
            Files = files,
            GotoCensus = census,
            FlagReadCensus = flagReadCensus,
            WebCensus = webCensus,
            ConstantMemoryAccesses = constantMemoryAccesses,
            DataSymbolLabels = DataSymbolLabels(prepared, analysis.GlobalClasses),
            AddressMap = addressMap
        };
    }

    /// <summary>The effects to give the method's return exits in a second prepare round: every
    /// return exit that still reads every value gets one entry in the map, reading the union of
    /// its method entries' observed <see cref="IrCallingConventions.OutputsOf"/> plus every value
    /// the conventions do not describe (both tokens read, nothing written, no fault). The observed
    /// demand - passed as <paramref name="conventions"/> - adds the terminal-observation demand to
    /// the code demand, so a value only the final machine state reads is still written back at the
    /// exit without becoming a signature parameter or output. Empty when no return exit
    /// shrinks.</summary>
    /// <summary>The facts a prepare round consumed for a method: the read set every return exit
    /// was given, the reads and may-write every summarized call was given, and the segment entry
    /// facts the method's entry versions resolved through. Rounds after the second widen these
    /// with anything the recomputed conventions demand of them; the entry facts are replaced by
    /// what the recomputed summaries still prove.</summary>
    private sealed class UsedFacts {
        public Dictionary<ICfgNode, CpuValues> ExitReads { get; } = new();
        public Dictionary<ICfgNode, CpuValues> CallReads { get; } = new();
        public Dictionary<ICfgNode, CpuValues> CallMayWrite { get; } = new();
        public Dictionary<ICfgNode, IrEffects> CallSummaries { get; } = new();
        public Dictionary<ICfgNode, CpuValues> FaultReads { get; } = new();
        public Dictionary<ICfgNode, IrSegmentEntryFact> EntryFacts { get; } = new();
    }

    /// <summary>The segment entry facts the summaries prove at the entries of
    /// <paramref name="prepared"/>'s method, keyed by the entry node so they survive a
    /// re-prepare: an entry a non-call way reaches, or whose call sites disagree, carries no
    /// fact and its entry versions stay symbolic.</summary>
    private static IReadOnlyDictionary<ICfgNode, IrSegmentEntryFact> EntryFactsOf(
        PreparedMethod prepared, IrSegmentSummaries summaries) {
        IrSegmentFacts proved = summaries.FactsFor(prepared.Method);
        Dictionary<ICfgNode, IrSegmentEntryFact> facts = new();
        foreach (IrEntry entry in prepared.IrMethod.Entries) {
            if (proved.EntryFactOf(entry.Block) is { } fact) {
                facts[entry.Node] = fact;
            }
        }
        return facts;
    }

    /// <summary>The reads every return or resolved-transfer exit of <paramref name="prepared"/>
    /// demands under <paramref name="conventions"/>. A return exit reads the union of its method
    /// entries' observed <see cref="IrCallingConventions.OutputsOf"/> plus every value the
    /// conventions do not describe plus the landing inputs the method changed; a resolved
    /// transfer reads the union of its targets' observed inputs plus the non-reportable values.
    /// Includes exits that still demand every value - the fixed point needs the full demand to
    /// detect growth back to it.</summary>
    private static IReadOnlyDictionary<ICfgNode, CpuValues> ExitReadsOf(
        PreparedMethod prepared, IrCallingConventions conventions) {
        string methodName = prepared.Method.MethodName;
        List<IrExit> exits = prepared.IrMethod.Blocks
            .Where(block => block.HasTerminator && block.Terminator is IrExit exit
                && (conventions.IsReturnExit(methodName, exit)
                    || conventions.IsResolvedTransferExit(methodName, exit)))
            .Select(block => (IrExit)block.Terminator).ToList();
        if (exits.Count == 0) {
            return new Dictionary<ICfgNode, CpuValues>();
        }
        CpuValues outputs = CpuValues.None;
        CpuValues changes = CpuValues.None;
        foreach (CfgCodePartitionEntry entry in prepared.Method.Entries) {
            outputs |= conventions.OutputsOf(entry);
            changes |= conventions.EffectOf(entry).Writes;
        }
        CpuValues nonReportable = CpuValues.All & ~IrCallingConventions.Reportable;
        Dictionary<ICfgNode, CpuValues> reads = new();
        foreach (IrExit exit in exits) {
            CpuValues demand = conventions.IsResolvedTransferExit(methodName, exit)
                ? conventions.TransferInputsOf(methodName, exit) | nonReportable
                : outputs | nonReportable
                    | (conventions.LandingInputsOf(methodName, exit) & changes);
            reads[exit.Origin] = reads.GetValueOrDefault(exit.Origin) | demand;
        }
        return reads;
    }

    /// <summary>The effects to give return and resolved-transfer exits in a prepare round: both
    /// tokens read, nothing written, no fault. Exits whose demand is every value get
    /// <see cref="IrEffects.ExitUse"/> explicitly - the flattener's own default for an
    /// <see cref="InterruptReturnNode"/> or <see cref="FailAsUntestedNode"/> transfer is the
    /// transfer's partial effect set, which is below the demand.</summary>
    private static IReadOnlyDictionary<ICfgNode, IrEffects> ExitEffectsFromReads(
        IReadOnlyDictionary<ICfgNode, CpuValues> readsByExit) {
        Dictionary<ICfgNode, IrEffects> effects = new();
        foreach ((ICfgNode origin, CpuValues reads) in readsByExit) {
            effects[origin] = reads == CpuValues.All
                ? IrEffects.ExitUse
                : new IrEffects(reads, CpuValues.None, CpuValues.None,
                    IrTokens.All, IrTokens.None, false, false);
        }
        return effects.Count == 0 ? IrLifter.NoEffects : effects;
    }

    /// <summary>The effects to give summarized calls in a prepare round after the second: the
    /// union of what every earlier round demanded of them, over the last summary's shape
    /// (tokens, fault, interrupt delivery).</summary>
    private static IReadOnlyDictionary<ICfgNode, IrEffects> CallEffectsFromUsed(UsedFacts facts) {
        Dictionary<ICfgNode, IrEffects> effects = new();
        foreach ((ICfgNode origin, IrEffects summary) in facts.CallSummaries) {
            effects[origin] = new IrEffects(facts.CallReads[origin], CpuValues.None,
                facts.CallMayWrite[origin], IrTokens.All, IrTokens.All, summary.MayFault, true);
        }
        return effects.Count == 0 ? IrLifter.NoEffects : effects;
    }

    /// <summary>Every method whose consumed facts are below what <paramref name="conventions"/>
    /// demands of them: a return exit whose reads grew, or a summarized call whose target inputs
    /// or changes grew - or that the conventions no longer summarize at all, which drops the
    /// summary so the call goes back to <see cref="IrEffects.Unknown"/>. The used sets are
    /// widened in place with the new demands.</summary>
    private static List<(int Index, string Reason)> ConsumeViolations(
        List<PreparedMethod> prepared, UsedFacts?[] used,
        IrCallingConventions conventions, IrSegmentSummaries summaries, CallEffect contract,
        CfgGeneratorContext context) {
        List<(int Index, string Reason)> violated = new();
        for (int i = 0; i < prepared.Count; i++) {
            UsedFacts? facts = used[i];
            if (facts is null) {
                continue;
            }
            List<string> reasons = new();
            foreach ((ICfgNode origin, CpuValues demand) in
                     ExitReadsOf(prepared[i], conventions)) {
                CpuValues was = facts.ExitReads.GetValueOrDefault(origin, CpuValues.All);
                CpuValues grew = demand & ~was;
                if (grew != CpuValues.None) {
                    facts.ExitReads[origin] = was | demand;
                    reasons.Add($"exit {origin.Address} reads grew by {grew}");
                }
            }
            if (facts.CallSummaries.Count > 0) {
                string methodName = prepared[i].Method.MethodName;
                Dictionary<ICfgNode, object> callSites = new();
                foreach (IrBlock block in prepared[i].IrMethod.Blocks) {
                    foreach (IrStatement statement in block.Statements) {
                        if (statement.Node is CheckExternalEventsNode
                            || statement.Origin is not CfgInstruction { IsCall: true }) {
                            continue;
                        }
                        callSites.TryAdd(statement.Origin, statement);
                    }
                    if (block.HasTerminator && block.Terminator is not IrExit
                        && block.Terminator.Origin is CfgInstruction { IsCall: true }) {
                        callSites.TryAdd(block.Terminator.Origin, block.Terminator);
                    }
                }
                foreach (ICfgNode origin in facts.CallSummaries.Keys.ToList()) {
                    if (!callSites.TryGetValue(origin, out object? site)) {
                        continue;
                    }
                    (IrEffects? effect, IrCallingConventions.CallSummarySkip skip) =
                        site is IrStatement statement
                            ? conventions.CallSummaryOf(methodName, statement, contract)
                            : conventions.CallSummaryOf(methodName, (IrTerminator)site, contract);
                    if (effect is null) {
                        facts.CallSummaries.Remove(origin);
                        facts.CallReads.Remove(origin);
                        facts.CallMayWrite.Remove(origin);
                        reasons.Add($"call {origin.Address} is no longer summarized ({skip})");
                        continue;
                    }
                    CpuValues readsGrew = effect.Value.Reads & ~facts.CallReads[origin];
                    CpuValues mayGrew = effect.Value.MayWrite & ~facts.CallMayWrite[origin];
                    if (readsGrew != CpuValues.None || mayGrew != CpuValues.None) {
                        facts.CallReads[origin] |= effect.Value.Reads;
                        facts.CallMayWrite[origin] |= effect.Value.MayWrite;
                        reasons.Add($"call {origin.Address} summary grew reads {readsGrew}, may-write {mayGrew}");
                    }
                }
            }
            IReadOnlyDictionary<ICfgNode, CpuValues> faultDemand =
                FaultReadsOf(prepared[i], conventions, context);
            foreach (ICfgNode origin in facts.FaultReads.Keys.ToList()) {
                if (!faultDemand.ContainsKey(origin)) {
                    facts.FaultReads.Remove(origin);
                    reasons.Add($"fault {origin.Address} is no longer resolved (reads every value)");
                }
            }
            foreach ((ICfgNode origin, CpuValues demand) in faultDemand) {
                CpuValues was = facts.FaultReads.GetValueOrDefault(origin, CpuValues.All);
                CpuValues grew = demand & ~was;
                if (grew != CpuValues.None) {
                    facts.FaultReads[origin] = was | demand;
                    reasons.Add($"fault {origin.Address} reads grew by {grew}");
                }
            }
            // A method prepared with entry values S is valid only while the recomputed
            // summaries still prove S at its entries; the consumed set is replaced by what the
            // summaries prove now, and a lost value re-prepares the method.
            if (EntryFactsChanged(prepared[i], facts, summaries, out string entryReason)) {
                reasons.Add(entryReason);
            }
            if (reasons.Count > 0) {
                violated.Add((i, string.Join("; ", reasons)));
            }
        }
        return violated;
    }

    /// <summary>Whether any consumed entry value of <paramref name="prepared"/> is no longer
    /// proved by <paramref name="summaries"/>: the consumed set is then invalid and the method
    /// is prepared again. The used set is replaced by the values the summaries prove now, grown
    /// or shrunk, so the next round consumes exactly what holds.</summary>
    private static bool EntryFactsChanged(PreparedMethod prepared, UsedFacts facts,
        IrSegmentSummaries summaries, out string reason) {
        reason = "";
        if (facts.EntryFacts.Count == 0) {
            return false;
        }
        IReadOnlyDictionary<ICfgNode, IrSegmentEntryFact> proved =
            EntryFactsOf(prepared, summaries);
        List<string> lost = [];
        foreach ((ICfgNode node, IrSegmentEntryFact consumed) in facts.EntryFacts) {
            if (!proved.TryGetValue(node, out IrSegmentEntryFact? current)
                || !IrSegmentFacts.FactEquals(consumed, current)) {
                lost.Add(node.Address.ToString());
            }
        }
        facts.EntryFacts.Clear();
        foreach ((ICfgNode node, IrSegmentEntryFact fact) in proved) {
            facts.EntryFacts[node] = fact;
        }
        if (lost.Count == 0) {
            return false;
        }
        reason = $"entry segment values at {string.Join(", ", lost)} are no longer proved";
        return true;
    }

    /// <summary>The effects to give the method's call statements in a second prepare round: the
    /// summary the observed conventions compute for every resolved call whose target returns,
    /// lands nowhere redirected and cannot reach a fault edge. Call sites that keep
    /// <see cref="IrEffects.Unknown"/> are counted by reason in <paramref name="skips"/>.</summary>
    private static IReadOnlyDictionary<ICfgNode, IrEffects> CallEffectsOf(
        PreparedMethod prepared, IrCallingConventions conventions, CallEffect contract,
        Dictionary<IrCallingConventions.CallSummarySkip, int> skips) {
        Dictionary<ICfgNode, IrEffects> effects = new();
        string methodName = prepared.Method.MethodName;
        foreach (IrBlock block in prepared.IrMethod.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                if (statement.Node is CheckExternalEventsNode
                    || statement.Origin is not CfgInstruction { IsCall: true }) {
                    continue;
                }
                (IrEffects? effect, IrCallingConventions.CallSummarySkip skip) =
                    conventions.CallSummaryOf(methodName, statement, contract);
                if (effect is { } summary) {
                    effects[statement.Origin] = summary;
                } else {
                    skips[skip] = skips.GetValueOrDefault(skip) + 1;
                }
            }
            if (block.HasTerminator && block.Terminator is not IrExit
                && block.Terminator.Origin is CfgInstruction { IsCall: true }) {
                (IrEffects? effect, IrCallingConventions.CallSummarySkip skip) =
                    conventions.CallSummaryOf(methodName, block.Terminator, contract);
                if (effect is { } summary) {
                    effects[block.Terminator.Origin] = summary;
                } else {
                    skips[skip] = skips.GetValueOrDefault(skip) + 1;
                }
            }
        }
        return effects.Count == 0 ? IrLifter.NoEffects : effects;
    }

    /// <summary>The fault-path read set of every faulting statement whose observed handlers and
    /// resume targets all resolved: the union of the handlers' and resume entries' observed inputs,
    /// the fault frame's reads (the pushed FLAGS, CS and the stack machinery) and every value outside
    /// the convention model. A site with an unresolved handler or resume target keeps reading every
    /// value. Keyed by instruction origin - every statement of a faulting instruction gets the set.</summary>
    private static IReadOnlyDictionary<ICfgNode, CpuValues> FaultReadsOf(
        PreparedMethod prepared, IrCallingConventions conventions, CfgGeneratorContext context) {
        Dictionary<ICfgNode, CpuValues> reads = new();
        HashSet<ICfgNode> unresolved = [];
        foreach (IrBlock block in prepared.IrMethod.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                if (!statement.Effects.MayFault || statement.Origin is not CfgInstruction instruction
                    || reads.ContainsKey(instruction) || !unresolved.Add(instruction)) {
                    continue;
                }
                if (FaultReadsOfSite(instruction, conventions, context) is { } set) {
                    reads[instruction] = set;
                }
            }
        }
        return reads.Count == 0 ? IrLifter.NoFaultReads : reads;
    }

    /// <summary>The fault-path read set of one faulting instruction, or <see langword="null"/> when
    /// a fault handler or a resume target of it is unresolved.</summary>
    private static CpuValues? FaultReadsOfSite(CfgInstruction instruction,
        IrCallingConventions conventions, CfgGeneratorContext context) {
        CpuValues reads = CpuValues.StatusFlags | CpuValues.Direction
            | CpuValues.Cs | CpuValues.Ss | CpuValues.Sp
            | (CpuValues.All & ~IrCallingConventions.Reportable);
        foreach (ResolvedCfgEdge edge in
                 context.GetSuccessorEdges(instruction, InstructionSuccessorType.CpuFault)) {
            CfgCodePartition handlerPartition = context.GetPartition(edge.Target);
            CfgCodePartitionEntry? handlerEntry = context.GetEntries(handlerPartition)
                .Where(entry => entry.Address == edge.Target.Address)
                .Cast<CfgCodePartitionEntry?>()
                .FirstOrDefault();
            if (handlerEntry is null) {
                return null;
            }
            reads |= conventions.InputsOf(handlerEntry);
            CfgGeneratorContext.LandingResolution resolution =
                context.ResolveFaultResumeTargets(handlerPartition);
            if (resolution.UnresolvedTargets.Count > 0) {
                return null;
            }
            foreach ((CfgCodePartition _, CfgCodePartitionEntry resumeEntry) in resolution.Entries) {
                reads |= conventions.InputsOf(resumeEntry);
            }
        }
        return reads;
    }

    /// <summary>Verify mode: every faulting statement's <see cref="IrEffects.FaultReads"/> covers
    /// the fault-path demand the final conventions compute for it - a byte the set missed could
    /// sit in a promoted local whose write-back the statement then never emits before the fault.</summary>
    private static void VerifyObservedFaultReads(List<PreparedMethod> prepared,
        UsedFacts?[] used, IrCallingConventions conventions, CfgGeneratorContext context) {
        for (int i = 0; i < prepared.Count; i++) {
            if (used[i] is not { } facts || facts.FaultReads.Count == 0) {
                continue;
            }
            IReadOnlyDictionary<ICfgNode, CpuValues> demand =
                FaultReadsOf(prepared[i], conventions, context);
            foreach (IrBlock block in prepared[i].IrMethod.Blocks) {
                foreach (IrStatement statement in block.Statements) {
                    if (!demand.TryGetValue(statement.Origin, out CpuValues required)
                        || statement.Effects.FaultReads is not { } given
                        || (required & ~given) == CpuValues.None) {
                        continue;
                    }
                    throw new InvalidOperationException(
                        $"Faulting statement of {prepared[i].Method.MethodName} reads {given} on" +
                        $" its fault path, below the observed demand {required}.");
                }
            }
        }
    }

    /// <summary>Verify mode: every summarized call statement's reads cover the observed inputs of
    /// its targets - a target input the summary missed could sit in a promoted local whose
    /// write-back the caller then never emits before the call.</summary>
    private static void VerifyObservedCallReads(List<PreparedMethod> prepared,
        IReadOnlyList<IReadOnlyDictionary<ICfgNode, IrEffects>> callEffectsByMethod,
        IrCallingConventions conventions) {
        for (int i = 0; i < prepared.Count; i++) {
            if (callEffectsByMethod[i].Count == 0) {
                continue;
            }
            string methodName = prepared[i].Method.MethodName;
            foreach (IrBlock block in prepared[i].IrMethod.Blocks) {
                foreach (IrStatement statement in block.Statements) {
                    if (!callEffectsByMethod[i].ContainsKey(statement.Origin)
                        || !IrAstChildren.Descendants(statement.Node).OfType<ProcedureCallHelperNode>()
                            .Any(call => call.Summary is not null)) {
                        continue;
                    }
                    CpuValues inputs = conventions.CallTargetsInputsOf(methodName, statement);
                    if ((inputs & ~statement.Effects.Reads) != CpuValues.None) {
                        throw new InvalidOperationException(
                            $"Resolved call of {methodName} reads {statement.Effects.Reads}," +
                            $" below its targets' observed inputs {inputs}.");
                    }
                }
                if (block.HasTerminator
                    && callEffectsByMethod[i].ContainsKey(block.Terminator.Origin)
                    && IrAstChildren.TerminatorNodes(block.Terminator).Any(node
                        => node is ProcedureCallHelperNode { Summary: not null }
                            or ProcedureCallReturnedElsewhereHelperNode { Summary: not null })) {
                    CpuValues inputs = conventions.CallTargetsInputsOf(methodName, block.Terminator);
                    if ((inputs & ~block.Terminator.Effects.Reads) != CpuValues.None) {
                        throw new InvalidOperationException(
                            $"Resolved call of {methodName} reads {block.Terminator.Effects.Reads}," +
                            $" below its targets' observed inputs {inputs}.");
                    }
                }
            }
        }
    }

    /// <summary>Verify mode: every return or resolved-transfer exit's reads cover the promotable
    /// part of its observed demand - the observed outputs, the non-reportable values and the
    /// landing or transfer-target inputs the exit must deliver. A register of that demand held in
    /// a promoted local then has an implicit use at the exit, which the write-back planning either
    /// serves (<c>AX = ax;</c> before the ret) or refuses the promotion for; a read set below the
    /// demand would let the promotion through without the write-back and the target or the final
    /// machine state would lose the register. Only the promotable bytes are checked: the rest of
    /// the demand lives in the machine registers, which are never locals.</summary>
    private static void VerifyObservedExitReads(List<PreparedMethod> prepared,
        IrCallingConventions conventions) {
        CpuValues nonReportable = CpuValues.All & ~IrCallingConventions.Reportable;
        foreach (PreparedMethod method in prepared) {
            string methodName = method.Method.MethodName;
            CpuValues outputs = CpuValues.None;
            CpuValues changes = CpuValues.None;
            foreach (CfgCodePartitionEntry entry in method.Method.Entries) {
                outputs |= conventions.OutputsOf(entry);
                changes |= conventions.EffectOf(entry).Writes;
            }
            foreach (IrBlock block in method.IrMethod.Blocks) {
                if (!block.HasTerminator || block.Terminator is not IrExit exit) {
                    continue;
                }
                CpuValues required;
                if (conventions.IsResolvedTransferExit(methodName, exit)) {
                    required = (conventions.TransferInputsOf(methodName, exit) | nonReportable)
                        & RegisterLocalPromotion.PromotableMask;
                } else if (conventions.IsReturnExit(methodName, exit)) {
                    required = (outputs | nonReportable
                        | (conventions.LandingInputsOf(methodName, exit) & changes))
                        & RegisterLocalPromotion.PromotableMask;
                } else {
                    continue;
                }
                if ((required & ~exit.Effects.Reads) != CpuValues.None) {
                    throw new InvalidOperationException(
                        $"Exit of {methodName} reads {exit.Effects.Reads}," +
                        $" below its observed demand {required}: a promoted local could lose" +
                        " its write-back to the machine state.");
                }
            }
        }
    }

    /// <summary>Applies DOS call comments, single-case switch simplification and direction-flag
    /// step folding to the prepared bodies.</summary>
    private static void RewritePreparedBodies(
        List<PreparedMethod> prepared, CfgPartitionedProgram program, ProgramAnalysis analysis) {
        ApplyDosCallComments(prepared, program, analysis.SegmentSummaries);
        for (int i = 0; i < prepared.Count; i++) {
            PreparedMethod method = prepared[i];
            prepared[i] = method with { Body = SingleCaseSwitchRewriter.Apply(method.Body) };
        }
        ApplyDirectionSteps(prepared, analysis.SegmentSummaries);
    }

    /// <summary>Folds each direction-flag step the entry summaries prove constant. Runs after
    /// <see cref="ProgramAnalysis"/> because the direction at a partition entry comes from the
    /// program-wide call-graph meet.</summary>
    private static void ApplyDirectionSteps(
        List<PreparedMethod> prepared, IrSegmentSummaries summaries) {
        for (int i = 0; i < prepared.Count; i++) {
            PreparedMethod preparedMethod = prepared[i];
            BlockNode body = IrEntryDirectionSteps.Apply(preparedMethod.IrMethod,
                summaries.ValuesFor(preparedMethod.Method), preparedMethod.Body);
            if (!ReferenceEquals(body, preparedMethod.Body)) {
                prepared[i] = preparedMethod with { Body = body };
            }
        }
    }

    /// <summary>Prints a <c>// DOS ...</c> comment before each <c>int 21h</c> the constant facts and
    /// the segment values fully describe, from bytes the changed-memory snapshot proves untouched.
    /// A snapshot that proves no byte unchanged gives no comments.</summary>
    private static void ApplyDosCallComments(
        List<PreparedMethod> prepared,
        CfgPartitionedProgram program, IrSegmentSummaries summaries) {
        for (int i = 0; i < prepared.Count; i++) {
            PreparedMethod preparedMethod = prepared[i];
            Dictionary<SegmentedAddress, DosCallResolution> resolutions = DosCallComments.Resolve(
                preparedMethod, summaries.ValuesFor(preparedMethod.Method),
                program.Inputs.ChangedMemory, program.Inputs.MemorySnapshot);
            if (resolutions.Count > 0) {
                prepared[i] = preparedMethod with {
                    Body = DosCallComments.Apply(preparedMethod.Body, resolutions)
                };
            }
        }
    }

    /// <summary>
    /// One label per location-class property at its proven address, and one per CS global access
    /// still printed through the register class: the only data symbols with a known address this
    /// generation produces.
    /// </summary>
    private static List<DataSymbolLabel> DataSymbolLabels(
        IReadOnlyList<PreparedMethod> prepared,
        GlobalAccessClassification classification) {
        List<DataSymbolLabel> labels = [];
        HashSet<(string Name, SegmentedAddress Address)> seen = [];
        foreach (GlobalAccessClassification.GlobalClassInfo classInfo in classification.Classes) {
            if (classInfo.SegmentValue is not { } segment) {
                continue;
            }
            foreach (GlobalAccessClassification.GlobalClassProperty property in
                classInfo.Properties.Values) {
                DataSymbolLabel label = new(
                    IrGlobalAccesses.PropertyName(property.Offset, property.Width),
                    new SegmentedAddress(segment, property.Offset));
                if (seen.Add((label.Name, label.Address))) {
                    labels.Add(label);
                }
            }
        }
        foreach (PreparedMethod preparedMethod in prepared) {
            ushort segment = preparedMethod.Method.PrimaryEntry.Address.Segment;
            foreach (ConstantMemoryAccess access in
                preparedMethod.Emission.ConstantMemoryAccesses.Entries.Select(entry => entry.Key)) {
                if (access.Segment != SegmentRegisterIndex.CsIndex) {
                    continue;
                }
                DataSymbolLabel label = new(IrGlobalAccesses.PropertyName(access),
                    new SegmentedAddress(segment, access.Offset));
                if (seen.Add((label.Name, label.Address))) {
                    labels.Add(label);
                }
            }
        }
        return labels;
    }

    /// <summary>
    /// The detail line a method slower than <see cref="SlowMethodThresholdMilliseconds"/> logs: its
    /// total time, its instruction, IR block and IR statement counts, and every measured step in
    /// execution order.
    /// </summary>
    internal static string SlowMethodLine(int current, int total, string name, int instructions, int blocks,
        int statements, GenerationStepTimer timer) =>
        $"Code generation: slow method {current} / {total} {name} took {timer.TotalMilliseconds} ms ({instructions} instructions, {blocks} IR blocks, {statements} statements): "
        + string.Join(", ", timer.Steps.Select(step => $"{step.Step} {step.Milliseconds} ms"));

    /// <summary>
    /// Lifts every method of <paramref name="program"/> to an <see cref="IrMethod"/>, building the analysis,
    /// context, plan and lowerer exactly like <see cref="Generate"/>. Only tests call it for now.
    /// </summary>
    internal IReadOnlyList<LiftedMethod> LiftMethods(CfgPartitionedProgram program) {
        InstructionMnemonic instructionMnemonic = new(_assemblyRenderer);
        GeneratorAnalysis analysis = GeneratorAnalysis.Build(program, instructionMnemonic, _policy);
        CfgGeneratorContext context = analysis.Context;
        GenerationPlan plan = GenerationPlanBuilder.Build(context);
        TransferEmitter transferEmitter = new(context);
        HardwareInterruptEffectBuilder effectBuilder =
            new(context, transferEmitter, _assemblyRenderer, plan);
        InstructionLowerer lowerer = new(context, transferEmitter, effectBuilder,
            effectBuilder.Compute());
        IrLifter lifter = new(context, lowerer, _assemblyRenderer);
        return plan.Methods.Select(method => new LiftedMethod(lifter.Lift(method), method, lowerer, context)).ToList();
    }

    /// <summary>Usings and the file-scoped namespace every generated file starts with.</summary>
    private static void EmitFilePreamble(CSharpSourceWriter writer, bool frameTypes) {
        EmitHeader(writer, frameTypes);
        writer.Line($"namespace {GeneratedOverrideNames.GeneratedNamespace};");
        writer.Line();
    }

    /// <summary>
    /// Builds the per-segment method files: methods keep plan order inside their segment, segments are
    /// emitted in ascending order, and a segment whose file would exceed the line limit is split at
    /// method boundaries into numbered parts.
    /// </summary>
    private List<GeneratedSourceFile> BuildSegmentFiles(
        List<(ushort Segment, string Text, bool HasFrame, string MethodName,
            IReadOnlyList<MethodInstructionLine> Instructions)> methodTexts,
        List<GeneratedAddressMapEntry> addressMap) {
        List<GeneratedSourceFile> files = [];
        foreach (var group in methodTexts
            .GroupBy(method => method.Segment)
            .OrderBy(group => group.Key)) {
            files.AddRange(BuildSegmentPartFiles(group.Key,
                group.Select(method => (method.Text, method.HasFrame, method.MethodName,
                    method.Instructions)).ToList(), addressMap));
        }
        return files;
    }

    /// <summary>
    /// Builds the support-stub files: every method the emulator wrote itself, in entry-address
    /// order, split at method boundaries on the same line limit as the segment files.
    /// </summary>
    private List<GeneratedSourceFile> BuildSupportStubFiles(
        List<(string Text, bool HasFrame, uint EntryLinear, string MethodName,
            IReadOnlyList<MethodInstructionLine> Instructions)> methodTexts,
        List<GeneratedAddressMapEntry> addressMap) {
        var chunks = methodTexts
            .OrderBy(method => method.EntryLinear)
            .Select(method => (method.Text, method.HasFrame, method.MethodName, method.Instructions))
            .ToList();
        if (chunks.Count == 0) {
            return [];
        }
        List<List<(string Text, bool HasFrame, string MethodName,
            IReadOnlyList<MethodInstructionLine> Instructions)>> parts = SplitChunks(chunks);
        List<GeneratedSourceFile> files = new(parts.Count);
        for (int i = 0; i < parts.Count; i++) {
            string name = GeneratedOverrideNames.SupportStubsFileName(i + 1, parts.Count);
            files.Add(new GeneratedSourceFile(name, BuildPartFileText(parts[i], name, addressMap)));
        }
        return files;
    }

    private List<GeneratedSourceFile> BuildSegmentPartFiles(ushort segment,
        List<(string Text, bool HasFrame, string MethodName,
            IReadOnlyList<MethodInstructionLine> Instructions)> chunks,
        List<GeneratedAddressMapEntry> addressMap) {
        var parts = SplitChunks(chunks);
        List<GeneratedSourceFile> files = new(parts.Count);
        for (int i = 0; i < parts.Count; i++) {
            string name = SegmentFileName(segment, i + 1, parts.Count);
            files.Add(new GeneratedSourceFile(name, BuildPartFileText(parts[i], name, addressMap)));
        }
        return files;
    }

    /// <summary>Assembles one segment or stubs file and records every instruction's 1-based line
    /// range of its methods into <paramref name="addressMap"/>.</summary>
    private static string BuildPartFileText(
        List<(string Text, bool HasFrame, string MethodName,
            IReadOnlyList<MethodInstructionLine> Instructions)> part, string fileName,
        List<GeneratedAddressMapEntry> addressMap) {
        string prefix = SegmentFilePrefix(part.Any(chunk => chunk.HasFrame));
        int startIndex = CountLines(prefix);
        StringBuilder text = new(prefix);
        foreach (var chunk in part) {
            foreach (MethodInstructionLine instruction in chunk.Instructions) {
                addressMap.Add(new GeneratedAddressMapEntry(instruction.Address.ToString(), fileName,
                    chunk.MethodName, startIndex + instruction.FirstLine + 1,
                    startIndex + instruction.LastLine + 1));
            }
            text.Append(chunk.Text);
            startIndex += CountLines(chunk.Text);
        }
        text.Append("}\n");
        return text.ToString();
    }

    private List<List<(string Text, bool HasFrame, string MethodName,
            IReadOnlyList<MethodInstructionLine> Instructions)>> SplitChunks(
        List<(string Text, bool HasFrame, string MethodName,
            IReadOnlyList<MethodInstructionLine> Instructions)> chunks) {
        int overhead = CountLines(SegmentFilePrefix(true)) + 1;
        List<List<(string Text, bool HasFrame, string MethodName,
            IReadOnlyList<MethodInstructionLine> Instructions)>> parts = [[]];
        int lines = overhead;
        foreach (var chunk in chunks) {
            int chunkLines = CountLines(chunk.Text);
            if (parts[^1].Count > 0 && lines + chunkLines > _maxLinesPerFile) {
                parts.Add([]);
                lines = overhead;
            }
            parts[^1].Add(chunk);
            lines += chunkLines;
        }
        return parts;
    }

    private static string SegmentFilePrefix(bool frameTypes) {
        CSharpSourceWriter writer = new();
        EmitFilePreamble(writer, frameTypes);
        writer.OpenBlock($"public partial class {GeneratedOverrideNames.OverrideClassName}");
        return writer.ToString();
    }

    private static string SegmentFileName(ushort segment, int part, int partCount) {
        if (partCount == 1) {
            return $"{GeneratedOverrideNames.OverrideClassName}.{segment:X4}.cs";
        }
        return $"{GeneratedOverrideNames.OverrideClassName}.{segment:X4}.{part}.cs";
    }

    private static int CountLines(string text) => text.Count(character => character == '\n');

    private static void EmitHeader(CSharpSourceWriter writer, bool frameTypes) {
        writer.Line("using Spice86.Core.CLI;");
        writer.Line("using Spice86.Core.Emulator.CPU.Exceptions;");
        writer.Line("using Spice86.Core.Emulator.Function;");
        writer.Line("using Spice86.Core.Emulator.ReverseEngineer;");
        writer.Line("using Spice86.Core.Emulator.VM;");
        if (frameTypes) {
            writer.Line("using Spice86.Core.Emulator.CPU.Registers;");
            writer.Line("using Spice86.Core.Emulator.Memory;");
        }
        writer.Line("using Microsoft.Extensions.Logging;");
        writer.Line("using Spice86.Shared.Emulator.Memory;");
        writer.Line("using System;");
        writer.Line("using System.Collections.Generic;");
        writer.Line();
    }

    private static void EmitSupplier(CSharpSourceWriter writer, GenerationPlan plan) {
        writer.OpenBlock($"public sealed class {GeneratedOverrideNames.SupplierClassName} : IOverrideSupplier");
        writer.OpenBlock("public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine)");
        writer.Line($"return new {GeneratedOverrideNames.OverrideClassName}(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration, programStartAddress).FunctionInformations;");
        writer.CloseBlock();
        writer.CloseBlock();
        writer.Line();
    }

    private static void EmitSegmentFields(CSharpSourceWriter writer, GenerationPlan plan) {
        foreach (SegmentFieldPlan segment in plan.SegmentFields) {
            writer.Line($"protected readonly ushort {segment.FieldName};");
        }
        writer.Line();
    }

    /// <summary>
    /// Declares one <c>static readonly</c> byte array per signature, so call sites reference a named field
    /// instead of repeating the bytes inline. Writes nothing when the program has no signature.
    /// </summary>
    private static void EmitSignatureFields(CSharpSourceWriter writer,
        IReadOnlyList<SignatureFieldPlan> signatureFields) {
        if (signatureFields.Count == 0) {
            return;
        }

        foreach (SignatureFieldPlan field in signatureFields) {
            string bytes = string.Join(", ", field.Bytes.Select(value => value is byte byteValue ? $"0x{byteValue:X2}" : "null"));
            writer.Line($"private static readonly byte?[] {field.FieldName} = [{bytes}];");
        }
        writer.Line();
    }

    /// <summary>
    /// One <c>SpeculativeRun_{offset}</c> plan per distinct merged speculative run of the prepared bodies:
    /// the runs the <c>VerifySpeculativeRunOrFail</c> call sites reference by field name instead of passing
    /// each signature inline. Runs are keyed by address and field list, so identical runs in different
    /// methods share one field. Runs sharing an offset append their signature field names joined by
    /// underscores, so each name remains a valid C# identifier.
    /// </summary>
    private static IReadOnlyList<SpeculativeRunFieldPlan> NameSpeculativeRunFields(
        List<PreparedMethod> prepared) {
        Dictionary<(SegmentedAddress Address, string Fields), IReadOnlyList<string>> runs = [];
        foreach (PreparedMethod preparedMethod in prepared) {
            foreach (SpeculativeGuardNode node in SpeculativeRuns(preparedMethod.Body)) {
                runs.TryAdd((node.Address, string.Join(",", node.SignatureFields)), node.SignatureFields);
            }
        }
        List<KeyValuePair<(SegmentedAddress, string), IReadOnlyList<string>>> ordered = runs
            .OrderBy(entry => entry.Key.Item1.Linear)
            .ThenBy(entry => entry.Key.Item2, StringComparer.Ordinal)
            .ToList();
        Dictionary<KeyValuePair<(SegmentedAddress, string), IReadOnlyList<string>>, string> names =
            DisambiguatedNames.Build(
                ordered,
                entry => $"SpeculativeRun_{entry.Key.Item1.Offset:X4}",
                entry => string.Join("_", entry.Value));
        Dictionary<(SegmentedAddress, string), string> nameByRun =
            ordered.ToDictionary(entry => entry.Key, entry => names[entry]);
        for (int i = 0; i < prepared.Count; i++) {
            PreparedMethod preparedMethod = prepared[i];
            prepared[i] = preparedMethod with {
                Body = (BlockNode)AssignRunFieldNames(preparedMethod.Body, nameByRun)
            };
        }
        return ordered
            .Select(entry => new SpeculativeRunFieldPlan(entry.Key.Item1, entry.Value, names[entry]))
            .ToList();
    }

    /// <summary><paramref name="node"/> with every merged <see cref="SpeculativeGuardNode"/> given
    /// the <see cref="SpeculativeGuardNode.RunFieldName"/> its run took.</summary>
    private static IVisitableAstNode AssignRunFieldNames(IVisitableAstNode node,
        IReadOnlyDictionary<(SegmentedAddress Address, string Fields), string> nameByRun) {
        IVisitableAstNode renamed = node is SpeculativeGuardNode { SignatureFields.Count: > 1 } run
            ? run with {
                RunFieldName = nameByRun[(run.Address, string.Join(",", run.SignatureFields))]
            }
            : node;
        return IrAstChildren.MapChildren(renamed, child => AssignRunFieldNames(child, nameByRun));
    }

    private static IEnumerable<SpeculativeGuardNode> SpeculativeRuns(IVisitableAstNode node) {
        Stack<IVisitableAstNode> pending = new();
        pending.Push(node);
        while (pending.Count > 0) {
            IVisitableAstNode current = pending.Pop();
            if (current is SpeculativeGuardNode { SignatureFields.Count: > 1 } run) {
                yield return run;
            }
            foreach (IVisitableAstNode child in CodeGenerationAst.ChildStatements(current)) {
                pending.Push(child);
            }
        }
    }

    /// <summary>
    /// Declares one <c>static readonly</c> jagged signature array per merged speculative run, so a
    /// <c>VerifySpeculativeRunOrFail</c> call passes one named field instead of building the array per call.
    /// </summary>
    private static void EmitSpeculativeRunFields(CSharpSourceWriter writer,
        IReadOnlyList<SpeculativeRunFieldPlan> runFields) {
        foreach (SpeculativeRunFieldPlan run in runFields) {
            writer.Line($"private static readonly byte?[][] {run.FieldName} = [{string.Join(", ", run.SignatureFields)}];");
        }
        if (runFields.Count > 0) {
            writer.Line();
        }
    }

    /// <summary>
    /// Declares one <c>private readonly</c> <c>Func&lt;int, Action&gt;</c> field per single-entry
    /// method, assigned once in the constructor, so every site that passes the method as a
    /// <c>Func&lt;int, Action&gt;</c> (registrations, call helpers, jump dispatch) shares the same
    /// delegate identity and <c>JumpDispatcher</c> cycle detection sees the same target. Writes
    /// nothing when the program has no single-entry method.
    /// </summary>
    private static void EmitEntryFields(CSharpSourceWriter writer, GenerationPlan plan,
        TypedSignatures typedSignatures) {
        List<string> fields = plan.OverrideRegistrations
            .Where(registration => registration.SingleEntry)
            .Select(registration => GeneratedOverrideNames.EntryFieldNameOf(
                typedSignatures.DelegateNameOf(registration.MethodName)))
            .ToList();
        if (fields.Count == 0) {
            return;
        }
        foreach (string field in fields) {
            writer.Line($"private readonly Func<int, Action> {field};");
        }
        writer.Line();
    }

    /// <summary>
    /// Declares one <c>protected readonly</c> field per globals class, named like its type; the
    /// constructor instantiates the register classes from <c>Memory</c> and
    /// <c>State.SegmentRegisters</c> and the location classes from <c>Memory</c> and
    /// <c>programStartSegment</c>. Writes nothing when the program has no global access.
    /// </summary>
    private static void EmitGlobalsFields(CSharpSourceWriter writer,
        IReadOnlyList<GlobalAccessClassification.GlobalClassInfo> classes,
        IReadOnlyList<GlobalAccessClassification.TableClassInfo> tableClasses) {
        foreach (GlobalAccessClassification.GlobalClassInfo classInfo in classes) {
            writer.Line($"protected readonly {classInfo.Name} {classInfo.Name};");
        }
        foreach (GlobalAccessClassification.TableClassInfo classInfo in tableClasses) {
            writer.Line($"protected readonly {classInfo.Name} {classInfo.Name};");
        }
        if (classes.Count + tableClasses.Count > 0) {
            writer.Line();
        }
    }

    /// <summary>
    /// Emits one globals class per class of the classification: the register classes the
    /// unclassified and register-class accesses print through, then the location classes sorted by
    /// value. Properties go through the segmented indexer path exactly like the accesses they
    /// replace: the linear <c>BaseAddress + offset</c> path of the base class is not equivalent
    /// where the segmented path checks the segment limit (see GlobalsAccessorEquivalenceTest).
    /// </summary>
    private static IEnumerable<GeneratedSourceFile> EmitGlobalsFiles(
        GlobalAccessClassification classification) {
        foreach (GlobalAccessClassification.GlobalClassInfo classInfo in classification.Classes) {
            yield return new GeneratedSourceFile($"{classInfo.Name}.cs",
                GlobalsClassText(classInfo, classification));
        }
    }

    private static string GlobalsClassText(GlobalAccessClassification.GlobalClassInfo classInfo,
        GlobalAccessClassification classification) {
        if (classInfo.Register is { } segment) {
            return EmitGlobalsClass(segment, classInfo.Name, classInfo, classification);
        }
        return EmitLocationClass(classInfo, classification);
    }

    private static string EmitGlobalsClass(SegmentRegisterIndex segment, string className,
        GlobalAccessClassification.GlobalClassInfo classInfo,
        GlobalAccessClassification classification) {
        CSharpSourceWriter writer = new();
        EmitGlobalsFileHeader(writer);
        writer.OpenBlock($"public sealed class {className} : {GlobalsBaseType(segment)}");
        writer.Line("private readonly IMemory _memory;");
        writer.Line("private readonly SegmentRegisters _segmentRegisters;");
        writer.Line();
        writer.Line($"private ushort Segment => _segmentRegisters.UInt16[(uint)SegmentRegisterIndex.{segment}];");
        writer.Line();
        writer.OpenBlock($"public {className}(IMemory memory, SegmentRegisters segmentRegisters) : {GlobalsBaseCall(segment)}");
        writer.Line("_memory = memory;");
        writer.Line("_segmentRegisters = segmentRegisters;");
        writer.CloseBlock();
        foreach (GlobalAccessClassification.GlobalClassProperty property in classInfo.Properties.Values) {
            writer.Line();
            writer.Line($"/// {SegmentName(segment)}:0x{property.Offset:X4}, whatever {SegmentName(segment)} holds at the access.");
            foreach (string inside in InsideTableComments(classInfo, property, classification)) {
                writer.Line(inside);
            }
            EmitGlobalsProperty(writer, property);
        }
        writer.CloseBlock();
        return writer.ToString();
    }

    /// <summary>
    /// Emits one location class: a fixed-base data structure whose <c>Segment</c> the constructor
    /// sets to the proven value (absolute outside a DOS-loaded image, an expression of
    /// <c>programStartSegment</c> inside one), and whose property doc comments list the properties
    /// of the other location classes over the same bytes.
    /// </summary>
    private static string EmitLocationClass(GlobalAccessClassification.GlobalClassInfo classInfo,
        GlobalAccessClassification classification) {
        CSharpSourceWriter writer = new();
        EmitGlobalsFileHeader(writer);
        writer.OpenBlock($"public sealed class {classInfo.Name} : MemoryBasedDataStructure");
        writer.Line("private readonly IMemory _memory;");
        writer.Line("private readonly ushort Segment;");
        writer.Line();
        writer.OpenBlock($"public {classInfo.Name}(IMemory memory, ushort programStartSegment) : base(memory, {LocationBaseAddress(classInfo)})");
        writer.Line("_memory = memory;");
        writer.Line($"Segment = {LocationSegmentExpression(classInfo)};");
        writer.CloseBlock();
        foreach (GlobalAccessClassification.GlobalClassProperty property in classInfo.Properties.Values) {
            writer.Line();
            string overlaps = OverlapsComment(classInfo, property, classification);
            if (overlaps.Length > 0) {
                writer.Line(overlaps);
            }
            foreach (string inside in InsideTableComments(classInfo, property, classification)) {
                writer.Line(inside);
            }
            EmitGlobalsProperty(writer, property);
        }
        writer.CloseBlock();
        return writer.ToString();
    }

    private static void EmitGlobalsFileHeader(CSharpSourceWriter writer) {
        writer.Line("using Spice86.Core.Emulator.CPU.Registers;");
        writer.Line("using Spice86.Core.Emulator.Memory;");
        writer.Line("using Spice86.Core.Emulator.ReverseEngineer.DataStructure;");
        writer.Line();
        writer.Line($"namespace {GeneratedOverrideNames.GeneratedNamespace};");
        writer.Line();
    }

    /// <summary>The segment value a location class holds (it is only null on a register class).</summary>
    private static ushort SegmentValueOf(GlobalAccessClassification.GlobalClassInfo classInfo) =>
        classInfo.SegmentValue
        ?? throw new InvalidOperationException("A location class holds a segment value.");

    private static ushort SegmentValueOf(GlobalAccessClassification.TableClassInfo classInfo) =>
        classInfo.SegmentValue
        ?? throw new InvalidOperationException("A location class holds a segment value.");

    /// <summary>The base-address expression of a location class: <c>Segment * 16</c> written with
    /// the same expression <see cref="LocationSegmentExpression"/> emits.</summary>
    private static string LocationBaseAddress(GlobalAccessClassification.GlobalClassInfo classInfo) =>
        LocationBaseAddress(classInfo.LocationValue, SegmentValueOf(classInfo));

    private static string LocationBaseAddress(DataSegmentValue locationValue, ushort segmentValue) {
        if (!locationValue.ImageRelative) {
            return $"0x{segmentValue * 16:X}";
        }
        int difference = locationValue.Value;
        return difference switch {
            0 => "(uint)(programStartSegment * 16)",
            > 0 => $"(uint)((programStartSegment + 0x{difference:X4}) * 16)",
            _ => $"(uint)((programStartSegment - 0x{-difference:X4}) * 16)"
        };
    }

    private static string LocationSegmentExpression(GlobalAccessClassification.GlobalClassInfo classInfo) =>
        LocationSegmentExpression(classInfo.LocationValue, SegmentValueOf(classInfo));

    private static string LocationSegmentExpression(DataSegmentValue locationValue, ushort segmentValue) {
        if (!locationValue.ImageRelative) {
            return $"0x{segmentValue:X4}";
        }
        int difference = locationValue.Value;
        return difference switch {
            0 => "programStartSegment",
            > 0 => $"(ushort)(programStartSegment + 0x{difference:X4})",
            _ => $"(ushort)(programStartSegment - 0x{-difference:X4})"
        };
    }

    /// <summary>The <c>/// Overlaps ...</c> comment of a location-class property: the properties of
    /// the other location classes covering the same linear bytes, at most 8 then
    /// <c>and N more</c>.</summary>
    private static string OverlapsComment(GlobalAccessClassification.GlobalClassInfo classInfo,
        GlobalAccessClassification.GlobalClassProperty property,
        GlobalAccessClassification classification) {
        List<string> overlaps = [];
        int extra = 0;
        (uint Start, uint End) range = LocationRange(SegmentValueOf(classInfo), property);
        foreach (GlobalAccessClassification.GlobalClassInfo other in classification.Classes) {
            if (other.SegmentValue is not { } otherSegment || ReferenceEquals(other, classInfo)) {
                continue;
            }
            foreach (GlobalAccessClassification.GlobalClassProperty otherProperty in
                other.Properties.Values) {
                (uint start, uint end) = LocationRange(otherSegment, otherProperty);
                if (start >= range.End || range.Start >= end) {
                    continue;
                }
                if (overlaps.Count < 8) {
                    overlaps.Add($"{other.Name}.{IrGlobalAccesses.PropertyName(otherProperty.Offset, otherProperty.Width)}");
                } else {
                    extra++;
                }
            }
        }
        if (overlaps.Count == 0) {
            return string.Empty;
        }
        string suffix = extra > 0 ? $" and {extra} more" : string.Empty;
        return $"/// Overlaps {string.Join(", ", overlaps)}{suffix}.";
    }

    private static (uint Start, uint End) LocationRange(ushort segment,
        GlobalAccessClassification.GlobalClassProperty property) =>
        ((uint)(segment * 16 + property.Offset), (uint)(segment * 16 + property.Offset + property.Width));

    private static string SegmentName(SegmentRegisterIndex segment) => segment switch {
        SegmentRegisterIndex.DsIndex => "DS",
        SegmentRegisterIndex.EsIndex => "ES",
        SegmentRegisterIndex.CsIndex => "CS",
        SegmentRegisterIndex.FsIndex => "FS",
        SegmentRegisterIndex.GsIndex => "GS",
        _ => segment.ToString()
    };

    private static string GlobalsBaseType(SegmentRegisterIndex segment) => segment switch {
        SegmentRegisterIndex.DsIndex => "MemoryBasedDataStructureWithDsBaseAddress",
        SegmentRegisterIndex.EsIndex => "MemoryBasedDataStructureWithEsBaseAddress",
        SegmentRegisterIndex.CsIndex => "MemoryBasedDataStructureWithCsBaseAddress",
        _ => "MemoryBasedDataStructureWithSegmentRegisterBaseAddress"
    };

    private static string GlobalsBaseCall(SegmentRegisterIndex segment) => segment switch {
        SegmentRegisterIndex.DsIndex or SegmentRegisterIndex.EsIndex or SegmentRegisterIndex.CsIndex =>
            "base(memory, segmentRegisters)",
        _ => $"base(memory, segmentRegisters, (uint)SegmentRegisterIndex.{segment})"
    };

    private static void EmitGlobalsProperty(CSharpSourceWriter writer,
        GlobalAccessClassification.GlobalClassProperty property) {
        string type = property.Width switch {
            1 => "byte",
            2 => "ushort",
            4 => "uint",
            _ => throw new NotSupportedException($"No global property type for a {property.Width}-byte access.")
        };
        string indexer = property.Width switch {
            1 => "UInt8",
            2 => "UInt16",
            4 => "UInt32",
            _ => throw new NotSupportedException($"No global property indexer for a {property.Width}-byte access.")
        };
        string segmented = $"_memory.{indexer}[Segment, 0x{property.Offset:X4}]";
        writer.Line($"public {type} {IrGlobalAccesses.PropertyName(property.Offset, property.Width)} {{ get => {segmented}; set => {segmented} = value; }}");
    }

    /// <summary>
    /// Emits one table class per table class of the classification, after the globals files: one
    /// get-only property per (base, element width), each backed by one table instance created in
    /// the constructor. The table indexers go through the segmented indexer, the same path the
    /// replaced accesses took.
    /// </summary>
    private static IEnumerable<GeneratedSourceFile> EmitTableFiles(
        GlobalAccessClassification classification) {
        foreach (GlobalAccessClassification.TableClassInfo classInfo in classification.TableClasses) {
            yield return new GeneratedSourceFile($"{classInfo.Name}.cs",
                EmitTableClass(classInfo, classification));
        }
    }

    private static string TableClassDeclaration(GlobalAccessClassification.TableClassInfo classInfo) {
        if (classInfo.Register is { } register) {
            return $"public sealed class {classInfo.Name} : {GlobalsBaseType(register)}";
        }
        return $"public sealed class {classInfo.Name} : MemoryBasedDataStructure";
    }

    private static string TableConstructor(GlobalAccessClassification.TableClassInfo classInfo) {
        if (classInfo.Register is { }) {
            return $"public {classInfo.Name}(IMemory memory, SegmentRegisters segmentRegisters) : {GlobalsBaseCall(classInfo.Register.Value)}";
        }
        return $"public {classInfo.Name}(IMemory memory, ushort programStartSegment) : base(memory, {LocationBaseAddress(classInfo.LocationValue, SegmentValueOf(classInfo))})";
    }

    private static string EmitTableClass(GlobalAccessClassification.TableClassInfo classInfo,
        GlobalAccessClassification classification) {
        CSharpSourceWriter writer = new();
        EmitTableFileHeader(writer);
        writer.OpenBlock(TableClassDeclaration(classInfo));
        writer.Line("private readonly IMemory _memory;");
        if (classInfo.Register is { } classRegister) {
            writer.Line("private readonly SegmentRegisters _segmentRegisters;");
            writer.Line();
            writer.Line($"private ushort Segment => _segmentRegisters.UInt16[(uint)SegmentRegisterIndex.{classRegister}];");
        } else {
            writer.Line("private readonly ushort Segment;");
        }
        writer.Line();
        string constructor = TableConstructor(classInfo);
        writer.OpenBlock(constructor);
        writer.Line("_memory = memory;");
        if (classInfo.Register is not null) {
            writer.Line("_segmentRegisters = segmentRegisters;");
        } else {
            writer.Line($"Segment = {LocationSegmentExpression(classInfo.LocationValue, SegmentValueOf(classInfo))};");
        }
        foreach (GlobalAccessClassification.TableClassProperty property in classInfo.Properties.Values) {
            writer.Line($"{IrGlobalAccesses.PropertyName(property.Base, property.Width)} = new {TableType(property.Width)}(_memory, () => Segment, 0x{property.Base:X4}, {property.Length});");
        }
        writer.CloseBlock();
        foreach (GlobalAccessClassification.TableClassProperty property in classInfo.Properties.Values) {
            writer.Line();
            writer.Line($"/// {SegmentDescription(classInfo)}:0x{property.Base:X4}, {property.Length} elements of {property.Width} byte{(property.Width > 1 ? "s" : string.Empty)}{TableSegmentSuffix(classInfo)}");
            string contains = TableContainsComment(classInfo, property, classification);
            if (contains.Length > 0) {
                writer.Line(contains);
            }
            writer.Line($"public {TableType(property.Width)} {IrGlobalAccesses.PropertyName(property.Base, property.Width)} {{ get; }}");
        }
        writer.CloseBlock();
        return writer.ToString();
    }

    private static void EmitTableFileHeader(CSharpSourceWriter writer) {
        writer.Line("using Spice86.Core.Emulator.CPU.Registers;");
        writer.Line("using Spice86.Core.Emulator.Memory;");
        writer.Line("using Spice86.Core.Emulator.ReverseEngineer.DataStructure;");
        writer.Line("using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;");
        writer.Line();
        writer.Line($"namespace {GeneratedOverrideNames.GeneratedNamespace};");
        writer.Line();
    }

    private static string TableType(int width) => width switch {
        1 => "UInt8SegmentedTable",
        2 => "UInt16SegmentedTable",
        4 => "UInt32SegmentedTable",
        _ => throw new NotSupportedException($"No table type for a {width}-byte element.")
    };

    /// <summary>The leading <c>DS</c> or <c>0x2000</c> of a table property doc comment.</summary>
    private static string SegmentDescription(GlobalAccessClassification.TableClassInfo classInfo) =>
        classInfo.Register is { } register ? SegmentName(register) : $"0x{SegmentValueOf(classInfo):X4}";

    private static string TableSegmentSuffix(GlobalAccessClassification.TableClassInfo classInfo) =>
        classInfo.Register is { } register ? $", whatever {SegmentName(register)} holds at the access." : ".";

    /// <summary>Whether a globals class and a table class describe the same memory: the same
    /// register class, or location classes over the same proven segment.</summary>
    private static bool SameClassGroup(GlobalAccessClassification.TableClassInfo tableClass,
        GlobalAccessClassification.GlobalClassInfo globalsClass) {
        if (tableClass.Register is { } register) {
            return globalsClass.Register == register;
        }
        return globalsClass.SegmentValue is not null && globalsClass.SegmentValue == tableClass.SegmentValue;
    }

    /// <summary>The <c>/// Contains ...</c> line of a table property: the globals properties of the
    /// class's class group whose bytes fall inside the table, at most 8 then
    /// <c>and N more</c>.</summary>
    private static string TableContainsComment(GlobalAccessClassification.TableClassInfo classInfo,
        GlobalAccessClassification.TableClassProperty property,
        GlobalAccessClassification classification) {
        List<string> contains = [];
        int extra = 0;
        int end = property.Base + property.Width * property.Length;
        foreach (GlobalAccessClassification.GlobalClassInfo globalsClass in classification.Classes) {
            if (!SameClassGroup(classInfo, globalsClass)) {
                continue;
            }
            foreach (GlobalAccessClassification.GlobalClassProperty global in globalsClass.Properties.Values) {
                if (global.Offset < property.Base || global.Offset + global.Width > end) {
                    continue;
                }
                int element = (global.Offset - property.Base) / property.Width;
                if (contains.Count < 8) {
                    contains.Add($"{globalsClass.Name}.{IrGlobalAccesses.PropertyName(global.Offset, global.Width)} (element {element})");
                } else {
                    extra++;
                }
            }
        }
        if (contains.Count == 0) {
            return string.Empty;
        }
        string suffix = extra > 0 ? $" and {extra} more" : string.Empty;
        return $"/// Contains {string.Join(", ", contains)}{suffix}.";
    }

    /// <summary>The <c>/// element N of ...</c> lines of a globals property: the tables of its class
    /// group whose bytes cover it, one line each.</summary>
    private static IEnumerable<string> InsideTableComments(
        GlobalAccessClassification.GlobalClassInfo globalsClass,
        GlobalAccessClassification.GlobalClassProperty property,
        GlobalAccessClassification classification) {
        foreach (GlobalAccessClassification.TableClassInfo tableClass in classification.TableClasses) {
            if (!SameClassGroup(tableClass, globalsClass)) {
                continue;
            }
            foreach (GlobalAccessClassification.TableClassProperty table in tableClass.Properties.Values) {
                int end = table.Base + table.Width * table.Length;
                if (property.Offset < table.Base || property.Offset + property.Width > end) {
                    continue;
                }
                int element = (property.Offset - table.Base) / table.Width;
                yield return $"/// element {element} of {tableClass.Name}.{IrGlobalAccesses.PropertyName(table.Base, table.Width)}.";
            }
        }
    }

    private static void EmitConstructor(CSharpSourceWriter writer, GenerationPlan plan,
        IReadOnlyList<GlobalAccessClassification.GlobalClassInfo> globalsClasses,
        IReadOnlyList<GlobalAccessClassification.TableClassInfo> tableClasses,
        TypedSignatures typedSignatures) {
        writer.OpenBlock($"public {GeneratedOverrideNames.OverrideClassName}(IDictionary<SegmentedAddress, FunctionInformation> functionInformations, Machine machine, ILogger loggerService, Configuration configuration, ushort programStartSegment) : base(functionInformations, machine, loggerService, configuration)");
        foreach (SegmentFieldPlan segment in plan.SegmentFields) {
            writer.Line($"{segment.FieldName} = {segment.Initializer};");
        }
        foreach (GlobalAccessClassification.GlobalClassInfo classInfo in globalsClasses) {
            if (classInfo.Register is not null) {
                writer.Line($"{classInfo.Name} = new {classInfo.Name}(Memory, State.SegmentRegisters);");
            } else {
                writer.Line($"{classInfo.Name} = new {classInfo.Name}(Memory, programStartSegment);");
            }
        }
        foreach (GlobalAccessClassification.TableClassInfo classInfo in tableClasses) {
            if (classInfo.Register is not null) {
                writer.Line($"{classInfo.Name} = new {classInfo.Name}(Memory, State.SegmentRegisters);");
            } else {
                writer.Line($"{classInfo.Name} = new {classInfo.Name}(Memory, programStartSegment);");
            }
        }
        writer.Line();
        foreach (string delegateName in plan.OverrideRegistrations
            .Where(registration => registration.SingleEntry)
            .Select(registration => typedSignatures.DelegateNameOf(registration.MethodName))) {
            writer.Line($"{GeneratedOverrideNames.EntryFieldNameOf(delegateName)} = _ => {delegateName}();");
        }
        foreach (OverrideRegistration registration in plan.OverrideRegistrations) {
            string symbolName = registration.SingleEntry || registration.LoadOffset == 0
                ? registration.BaseName
                : BuildSecondaryEntryName(registration.BaseName);
            writer.Line($"DefineFunctionWithoutInterpreterFallback({registration.SegmentVariable}, 0x{registration.Offset:X4}, {DelegateExpressionOf(registration, typedSignatures)}, \"{symbolName}\");");
        }
        writer.CloseBlock();
        writer.Line();
    }

    /// <summary>The <c>Func&lt;int, Action&gt;</c> expression a registration installs at its address:
    /// the cached entry delegate of a single-entry method, the method itself for a primary entry, or a
    /// <c>loadOffset</c>-passing lambda for a secondary entry.</summary>
    private static string DelegateExpressionOf(OverrideRegistration registration,
        TypedSignatures typedSignatures) {
        if (registration.SingleEntry) {
            return GeneratedOverrideNames.EntryFieldNameOf(typedSignatures.DelegateNameOf(registration.MethodName));
        }
        if (registration.LoadOffset == 0) {
            return registration.MethodName;
        }
        return $"_ => {registration.MethodName}(0x{registration.LoadOffset:X4})";
    }

    /// <summary>
    /// Emits the generated <c>HardwareInterruptFunction</c>: a switch over the (vector, handler) pairs
    /// discovery observed as hardware-interrupt deliveries, so a delivery to a handler the run never
    /// observed fails as untested instead of silently running whatever override happens to be registered
    /// there. The observed pairs are the deliveries <see cref="CfgCpu"/> recorded during the run
    /// (<see cref="HardwareInterruptSetup.HardwareInterruptDeliveries"/>) restricted to targets that got a
    /// generated entry; each arm returns the same entry delegate the constructor registers at that address.
    /// A program with no observed delivery emits only the failing default arm.
    /// </summary>
    private static void EmitHardwareInterruptDispatch(CSharpSourceWriter writer, CfgPartitionedProgram program,
        GenerationPlan plan, TypedSignatures typedSignatures) {
        HardwareInterruptSetup setup = program.Inputs.HardwareInterrupts;
        HashSet<SegmentedAddress> delivered = program.Partitions
            .SelectMany(partition => partition.Entries)
            .Where(entry => entry.Kind == CfgCodePartitionEntryKind.ExecutionContextEntry
                && !entry.Address.Equals(program.Inputs.ProgramEntryPoint))
            .Select(entry => entry.Address)
            .ToHashSet();
        Dictionary<SegmentedAddress, string> functionByAddress = plan.OverrideRegistrations
            .ToDictionary(registration => registration.Address,
                registration => DelegateExpressionOf(registration, typedSignatures));
        writer.Line("protected override Func<int, Action> HardwareInterruptFunction(byte vectorNumber, SegmentedAddress target) =>");
        writer.Indent();
        writer.Line("(vectorNumber, target.Segment, target.Offset) switch {");
        writer.Indent();
        foreach ((byte vector, SegmentedAddress target) in setup.HardwareInterruptDeliveries
            .OrderBy(delivery => delivery.Vector)
            .ThenBy(delivery => delivery.Target.Linear)
            .Distinct()) {
            if (delivered.Contains(target)
                && functionByAddress.TryGetValue(target, out string? function)) {
                writer.Line($"(0x{vector:X2}, 0x{target.Segment:X4}, 0x{target.Offset:X4}) => {function},");
            }
        }
        writer.Line("_ => throw FailAsUntested($\"Hardware interrupt 0x{vectorNumber:X2} targets {target}, which discovery did not observe as a handler of that vector\")");
        writer.Dedent();
        writer.Line("};");
        writer.Dedent();
        writer.Line();
    }

    /// <summary>
    /// Builds the address-free symbol name for a secondary partition entry by tagging the partition's base
    /// name with a single <c>_entry</c> marker. The tag is idempotent: a base name that already ends in
    /// <c>_entry</c> (a secondary entry promoted to its own partition root on a later generate cycle) is left
    /// unchanged, so the name reaches a fixed point through the Ghidra symbol round-trip instead of growing a
    /// new marker each cycle.
    /// </summary>
    private static string BuildSecondaryEntryName(string baseName) {
        if (baseName.EndsWith(EntrySuffix, StringComparison.Ordinal)) {
            return baseName;
        }
        return baseName + EntrySuffix;
    }
}
