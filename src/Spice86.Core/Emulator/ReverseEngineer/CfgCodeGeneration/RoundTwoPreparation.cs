namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.StateSerialization;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The second round of the method preparation of <see cref="CfgCSharpGenerator.Generate"/>: the
/// conventions of the first round code over-summarize calls and exits, so the methods are
/// prepared again under the conventions the prepared program actually observes - a fixed point
/// that runs until the prepared set is consistent with the conventions it computes.
/// </summary>
internal sealed class RoundTwoPreparation {
    /// <summary>The preparation cap: a fixpoint that did not converge after this many rounds is
    /// reported as a failure rather than prepared forever.</summary>
    private const int MaxPreparationRounds = 8;

    /// <summary>Prepares a second time every method whose round-two effects shrink the first
    /// round's universal demand, then re-prepares methods until the consumed facts cover what
    /// the recomputed conventions demand. <paramref name="prepared"/> is updated in place;
    /// returns the analysis of the final prepared set.</summary>
    public ProgramAnalysis Run(PreparedMethod[] prepared, GenerationPlan plan,
        GenerationStepTimer[] timers, IrMethodEmitter methodEmitter,
        CfgGeneratorContext context, HardwareInterruptEffectBuilder effectBuilder,
        CallEffect hardwareInterruptEffect, ProgramImages programImages, bool verify,
        ParallelOptions parallelOptions, DumpProgressReporter progress, ProgramAnalysis analysis) {
        // A return exit reads only the outputs of its method's entries (plus every value the
        // convention model does not describe and both tokens), and a resolved call reads and may
        // write only what its target's convention and the hardware contract describe: methods where
        // either set is smaller than a first-round universal effect are prepared a second time with
        // them, so the pipeline sees the real demand from the start.
        int reprepared = 0;
        Dictionary<IrCallingConventions.CallSummarySkip, int> callSkips = new();
        int summarizedCalls = 0;
        UsedFacts?[] used = new UsedFacts?[prepared.Length];
        List<IReadOnlyDictionary<ICfgNode, IrEffects>> callEffectsByMethod = new(prepared.Length);
        IReadOnlyDictionary<ICfgNode, IrEffects>?[] exitEffectsByMethod =
            new IReadOnlyDictionary<ICfgNode, IrEffects>?[prepared.Length];
        for (int i = 0; i < prepared.Length; i++) {
            IReadOnlyDictionary<ICfgNode, CpuValues> exitReads =
                ExitReadsOf(prepared[i], analysis.ObservedConventions,
                    hardwareInterruptEffect);
            IReadOnlyDictionary<ICfgNode, IrEffects> exitEffects = ExitEffectsFromReads(exitReads);
            exitEffectsByMethod[i] = exitEffects;
            IReadOnlyDictionary<ICfgNode, IrEffects> callEffects =
                CallEffectsOf(prepared[i], analysis.ObservedConventions,
                    hardwareInterruptEffect, callSkips);
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
        }
        Parallel.For(0, prepared.Length, parallelOptions, i => {
            if (used[i] is not { } facts) {
                return;
            }
            GenerationStepTimer timer = timers[i];
            MethodPlan methodPlan = plan.Methods[i];
            prepared[i] = timer.Measure("round 2 prepare",
                () => methodEmitter.Prepare(methodPlan, timer,
                    exitEffectsByMethod[i]!, callEffectsByMethod[i],
                    facts.FaultReads, facts.EntryFacts, analysis.TypedSignatures));
        });
        progress.Detail($"Resolved calls summarized: {summarizedCalls}; kept Unknown: "
            + string.Join(", ", callSkips
                .OrderBy(pair => pair.Key.ToString())
                .Select(pair => $"{pair.Key}={pair.Value}")));
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
                programImages, progress, parallelOptions);
            int rounds = 2;
            while (true) {
                List<(int Index, UsedFacts Facts, string Reason)> violated = ConsumeViolations(prepared, used,
                    current.ObservedConventions, current.SegmentSummaries,
                    hardwareInterruptEffect, context, current.TypedSignatures);
                if (violated.Count == 0) {
                    analysis = current;
                    break;
                }
                if (rounds >= MaxPreparationRounds) {
                    throw new InvalidOperationException(
                        $"Round-two conventions did not converge in {rounds} rounds: "
                        + string.Join("; ", violated.Select(violation =>
                            $"{prepared[violation.Index].Method.MethodName}: {violation.Reason}")));
                }
                rounds++;
                foreach ((int index, UsedFacts facts, _) in violated) {
                    exitEffectsByMethod[index] = ExitEffectsFromReads(facts.ExitReads);
                    callEffectsByMethod[index] = CallEffectsFromUsed(facts);
                }
                Parallel.ForEach(violated, parallelOptions, violation => {
                    int index = violation.Index;
                    UsedFacts facts = violation.Facts;
                    GenerationStepTimer timer = timers[index];
                    prepared[index] = timer.Measure($"round {rounds} prepare",
                        () => methodEmitter.Prepare(plan.Methods[index], timer,
                            exitEffectsByMethod[index]!, callEffectsByMethod[index], facts.FaultReads,
                            facts.EntryFacts, current.TypedSignatures));
                });
                progress.Detail($"Preparation round {rounds}");
                current = ProgramAnalysis.Compute(prepared, context, effectBuilder,
                    programImages, progress, parallelOptions);
            }
            if (verify) {
                VerifyObservedExitReads(prepared, analysis.ObservedConventions,
                    hardwareInterruptEffect);
                VerifyObservedCallReads(prepared, callEffectsByMethod, analysis.ObservedConventions);
                VerifyObservedFaultReads(prepared, used, analysis.ObservedConventions, context);
            }
            progress.Detail($"Round-two conventions computed over {reprepared} re-prepared methods in {rounds} rounds");
        }
        return analysis;
    }

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
        return prepared.IrMethod.Entries
            .Select(entry => proved.EntryFactOf(entry.Block) is { } fact
                ? (KeyValuePair<ICfgNode, IrSegmentEntryFact>?)new(entry.Node, fact)
                : null)
            .OfType<KeyValuePair<ICfgNode, IrSegmentEntryFact>>()
            .ToDictionary();
    }

    /// <summary>The reads every return or resolved-transfer exit of <paramref name="prepared"/>
    /// demands under <paramref name="conventions"/>. A return exit reads the union of its method
    /// entries' observed <see cref="IrCallingConventions.OutputsOf"/> plus every value the
    /// conventions do not describe plus the landing inputs the method's writes or restores carry:
    /// the preservation of a restored register depends on the pop that restores it, so the exit
    /// keeps that pop's write live even though the register is not in <c>Changes</c>. A resolved
    /// transfer reads the union of its targets' observed inputs plus the non-reportable values.
    /// Includes exits that still demand every value - the fixed point needs the full demand to
    /// detect growth back to it. A return also demands the hardware contract's reads: a handler
    /// delivered at an event check after the landing observes them.</summary>
    private static IReadOnlyDictionary<ICfgNode, CpuValues> ExitReadsOf(
        PreparedMethod prepared, IrCallingConventions conventions, CallEffect contract) {
        string methodName = prepared.Method.MethodName;
        List<(IrExit Exit, CpuValues? TerminalReads)> exits = prepared.IrMethod.Blocks
            .Where(block => block.HasTerminator && block.Terminator is IrExit)
            .Select(block => (IrExit)block.Terminator)
            .Select(exit => (Exit: exit, TerminalReads: conventions.TerminalExitReadsOf(methodName, exit)))
            .Where(pair => pair.TerminalReads.HasValue
                || conventions.IsReturnExit(methodName, pair.Exit)
                || conventions.IsResolvedTransferExit(methodName, pair.Exit))
            .ToList();
        if (exits.Count == 0) {
            return new Dictionary<ICfgNode, CpuValues>();
        }
        CpuValues outputs = CpuValues.None;
        CpuValues changes = CpuValues.None;
        foreach (CfgCodePartitionEntry entry in prepared.Method.Entries) {
            outputs |= conventions.OutputsOf(entry);
            changes |= conventions.EffectOf(entry).Writes;
        }
        CpuValues restored = RestoredPopWrites(prepared.IrMethod);
        CpuValues nonReportable = CpuValues.All & ~IrCallingConventions.Reportable;
        Dictionary<ICfgNode, CpuValues> reads = new();
        foreach ((IrExit exit, CpuValues? terminalReads) in exits) {
            CpuValues demand = terminalReads ?? (conventions.IsResolvedTransferExit(methodName, exit)
                ? conventions.TransferInputsOf(methodName, exit) | nonReportable
                : outputs | nonReportable
                    | ((conventions.LandingInputsOf(methodName, exit) | contract.Reads)
                        & (changes | restored)));
            reads[exit.Origin] = reads.GetValueOrDefault(exit.Origin) | demand;
        }
        return reads;
    }

    /// <summary>The general-register bytes <paramref name="method"/>'s pop statements write -
    /// the targets the dead-pop rewrite can reach: a pop into a register that is not in
    /// <c>Changes</c> still restores a preserved byte the landing observes.</summary>
    private static CpuValues RestoredPopWrites(IrMethod method) {
        CpuValues restored = CpuValues.None;
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                if (!IrPreservation.IsPopRestore(statement)
                    || statement.Node is not BinaryOperationNode {
                        BinaryOperation: BinaryOperation.ASSIGN,
                        Left: RegisterNode register }
                    || register is SegmentRegisterNode
                    || (register.DataType != DataType.UINT8
                        && register.RegisterIndex == (int)RegisterIndex.SpIndex)) {
                    continue;
                }
                restored |= IrValueSets.Of(register);
            }
        }
        return restored;
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
    private static List<(int Index, UsedFacts Facts, string Reason)> ConsumeViolations(
        PreparedMethod[] prepared, UsedFacts?[] used,
        IrCallingConventions conventions, IrSegmentSummaries summaries, CallEffect contract,
        CfgGeneratorContext context, TypedSignatures signatures) {
        List<(int Index, UsedFacts Facts, string Reason)> violated = new();
        for (int i = 0; i < prepared.Length; i++) {
            UsedFacts? facts = used[i];
            if (facts is null) {
                continue;
            }
            List<string> reasons = new();
            foreach ((ICfgNode origin, CpuValues demand) in
                     ExitReadsOf(prepared[i], conventions, contract)) {
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
                    // The call instruction lowers to several statements (boundary nodes, the call
                    // itself); the operand check needs the call node wherever it sits, so every
                    // statement of the origin is scanned rather than the one callSites kept.
                    foreach (ProcedureCallHelperNode call in prepared[i].IrMethod.Blocks
                             .SelectMany(block => block.Statements
                                 .Where(statement => ReferenceEquals(statement.Origin, origin))
                                 .SelectMany(statement => IrAstChildren.Descendants(statement.Node))
                                 .Concat(block.HasTerminator && ReferenceEquals(block.Terminator.Origin, origin)
                                     ? IrAstChildren.TerminatorNodes(block.Terminator)
                                     : []))
                             .OfType<ProcedureCallHelperNode>()) {
                        CpuValues expectedMask =
                            signatures.TypedCallOf(call) is { } callSignature
                                ? callSignature.Parameters.Aggregate(CpuValues.None,
                                    (set, parameter) => set | parameter.Mask)
                                : CpuValues.None;
                        if (call.TypedArgumentMask != expectedMask) {
                            reasons.Add($"call {origin.Address} typed arguments are out of date");
                        }
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
            foreach (ICfgNode origin in facts.FaultReads.Keys.ToList()
                    .Where(origin => !faultDemand.ContainsKey(origin))) {
                facts.FaultReads.Remove(origin);
                reasons.Add($"fault {origin.Address} is no longer resolved (reads every value)");
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
                violated.Add((i, facts, string.Join("; ", reasons)));
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
                    // Several statements can share one call instruction's origin (the sections of a
                    // target dispatch): the map is keyed by origin, so they carry the union - each
                    // section's real target is covered.
                    effects[statement.Origin] = effects.TryGetValue(statement.Origin, out IrEffects was)
                        ? IrEffects.Either(was, summary)
                        : summary;
                } else {
                    skips[skip] = skips.GetValueOrDefault(skip) + 1;
                }
            }
            if (block.HasTerminator && block.Terminator is not IrExit
                && block.Terminator.Origin is CfgInstruction { IsCall: true }) {
                (IrEffects? effect, IrCallingConventions.CallSummarySkip skip) =
                    conventions.CallSummaryOf(methodName, block.Terminator, contract);
                if (effect is { } summary) {
                    effects[block.Terminator.Origin] =
                        effects.TryGetValue(block.Terminator.Origin, out IrEffects was)
                            ? IrEffects.Either(was, summary)
                            : summary;
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
    private static void VerifyObservedFaultReads(PreparedMethod[] prepared,
        UsedFacts?[] used, IrCallingConventions conventions, CfgGeneratorContext context) {
        for (int i = 0; i < prepared.Length; i++) {
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
    private static void VerifyObservedCallReads(PreparedMethod[] prepared,
        IReadOnlyList<IReadOnlyDictionary<ICfgNode, IrEffects>> callEffectsByMethod,
        IrCallingConventions conventions) {
        for (int i = 0; i < prepared.Length; i++) {
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
                    CpuValues operandCovered = IrAstChildren.Descendants(statement.Node)
                        .OfType<ProcedureCallHelperNode>()
                        .Aggregate(CpuValues.None, (set, call) => set | call.TypedArgumentMask);
                    CpuValues inputs = conventions.CallTargetsInputsOf(methodName, statement);
                    if ((inputs & ~statement.Effects.Reads & ~operandCovered) != CpuValues.None) {
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
    private static void VerifyObservedExitReads(PreparedMethod[] prepared,
        IrCallingConventions conventions, CallEffect contract) {
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
                        | ((conventions.LandingInputsOf(methodName, exit) | contract.Reads)
                            & changes))
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
}
