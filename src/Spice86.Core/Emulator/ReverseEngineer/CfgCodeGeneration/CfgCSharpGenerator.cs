namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Microsoft.Extensions.Logging.Abstractions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Naming;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Linq;

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

    /// <summary>The production generator: no census collection, no IR verification.</summary>
    public CfgCSharpGenerator() : this(false, false) {
    }

    /// <summary>Creates a generator; production uses the parameterless options.</summary>
    /// <param name="collectCensuses">Whether each emitted method runs the census classifiers the
    /// generated-program diagnostics read; production skips them.</param>
    /// <param name="verify">Whether the IR invariants are checked after the passes; tests enable it.
    /// </param>
    public CfgCSharpGenerator(bool collectCensuses, bool verify)
        : this(collectCensuses, verify, MaxLinesPerFile) {
    }

    /// <summary>Creates a generator with a per-file line limit; tests use a small limit.</summary>
    internal CfgCSharpGenerator(bool collectCensuses, bool verify, int maxLinesPerFile) {
        _collectCensuses = collectCensuses;
        _verify = verify;
        _maxLinesPerFile = maxLinesPerFile;
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
        GeneratorAnalysis generatorAnalysis = GeneratorAnalysis.Build(program, instructionMnemonic);
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
            effectBuilder, _collectCensuses, _verify);

        List<GeneratedSourceFile> files = new(2 + plan.Methods.Count);

        CSharpSourceWriter supplierWriter = new();
        EmitFilePreamble(supplierWriter, false);
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

        ProgramAnalysis analysis = ProgramAnalysis.Compute(prepared, context, effectBuilder,
            program.Inputs.ProgramImages);
        progress.Detail($"Calling conventions fixed point finished in {analysis.CallingConventions.Iterations} iterations");

        RewritePreparedBodies(prepared, program, analysis);

        IReadOnlyList<SpeculativeRunFieldPlan> speculativeRunFields = NameSpeculativeRunFields(prepared);

        List<(ushort Segment, string Text, bool HasFrame)> methodTexts = new(plan.Methods.Count);
        for (int i = 0; i < prepared.Count; i++) {
            PreparedMethod preparedMethod = prepared[i];
            GenerationStepTimer timer = timers[i];
            CSharpSourceWriter methodWriter = new();
            methodWriter.Indent();
            AnalyzedMethod analyzed = methodEmitter.Analyze(preparedMethod, analysis, timer);
            methodEmitter.Print(methodWriter, analyzed, analysis, timer);
            methodTexts.Add((preparedMethod.Method.PrimaryEntry.Address.Segment, methodWriter.ToString(),
                preparedMethod.FramePlan is not null));
            MethodCensus methodCensus = preparedMethod.Emission.Census;
            census.Merge(methodCensus.Gotos);
            if (timer.TotalMilliseconds >= SlowMethodThresholdMilliseconds) {
                MethodPlan method = preparedMethod.Method;
                progress.Detail(SlowMethodLine(i + 1, prepared.Count, method.MethodName,
                    method.Nodes.OfType<CfgInstruction>().Count(),
                    methodCensus.LiftedBlocks, methodCensus.LiftedStatements, timer));
            }
        }
        files.Add(new GeneratedSourceFile(GeneratedOverrideNames.SupplierFileName, supplierWriter.ToString()));

        CSharpSourceWriter mainWriter = new();
        EmitFilePreamble(mainWriter, false);
        mainWriter.OpenBlock($"public partial class {GeneratedOverrideNames.OverrideClassName} : CSharpOverrideHelper");
        EmitSegmentFields(mainWriter, plan);
        EmitSignatureFields(mainWriter, plan.SignatureFields);
        EmitSpeculativeRunFields(mainWriter, speculativeRunFields);
        EmitEntryFields(mainWriter, plan, analysis.TypedSignatures);
        EmitGlobalsFields(mainWriter, analysis.GlobalClasses.Classes, analysis.GlobalClasses.TableClasses);
        EmitConstructor(mainWriter, plan, analysis.GlobalClasses.Classes, analysis.GlobalClasses.TableClasses,
            analysis.TypedSignatures);
        mainWriter.CloseBlock();
        files.Add(new GeneratedSourceFile(GeneratedOverrideNames.DumpFileSuffix, mainWriter.ToString()));
        files.AddRange(BuildSegmentFiles(methodTexts));
        files.AddRange(EmitGlobalsFiles(analysis.GlobalClasses));
        files.AddRange(EmitTableFiles(analysis.GlobalClasses));
        progress.EndPhase();

        return new GeneratedCSharpProgram {
            Files = files,
            GotoCensus = census,
            FlagReadCensus = flagReadCensus,
            WebCensus = webCensus,
            ConstantMemoryAccesses = constantMemoryAccesses,
            DataSymbolLabels = DataSymbolLabels(prepared, analysis.GlobalClasses)
        };
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
        $"Code generation: method {current} / {total} {name} took {timer.TotalMilliseconds} ms ({instructions} instructions, {blocks} IR blocks, {statements} statements): "
        + string.Join(", ", timer.Steps.Select(step => $"{step.Step} {step.Milliseconds} ms"));

    /// <summary>
    /// Lifts every method of <paramref name="program"/> to an <see cref="IrMethod"/>, building the analysis,
    /// context, plan and lowerer exactly like <see cref="Generate"/>. Only tests call it for now.
    /// </summary>
    internal IReadOnlyList<LiftedMethod> LiftMethods(CfgPartitionedProgram program) {
        InstructionMnemonic instructionMnemonic = new(_assemblyRenderer);
        GeneratorAnalysis analysis = GeneratorAnalysis.Build(program, instructionMnemonic);
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
        List<(ushort Segment, string Text, bool HasFrame)> methodTexts) {
        List<GeneratedSourceFile> files = [];
        foreach (IGrouping<ushort, (ushort Segment, string Text, bool HasFrame)> group in methodTexts
            .GroupBy(method => method.Segment)
            .OrderBy(group => group.Key)) {
            files.AddRange(BuildSegmentPartFiles(group.Key,
                group.Select(method => (method.Text, method.HasFrame)).ToList()));
        }
        return files;
    }

    private List<GeneratedSourceFile> BuildSegmentPartFiles(ushort segment,
        List<(string Text, bool HasFrame)> chunks) {
        int overhead = CountLines(SegmentFilePrefix(true)) + 1;
        List<List<(string Text, bool HasFrame)>> parts = [[]];
        int lines = overhead;
        foreach ((string Text, bool HasFrame) chunk in chunks) {
            int chunkLines = CountLines(chunk.Text);
            if (parts[^1].Count > 0 && lines + chunkLines > _maxLinesPerFile) {
                parts.Add([]);
                lines = overhead;
            }
            parts[^1].Add(chunk);
            lines += chunkLines;
        }
        List<GeneratedSourceFile> files = new(parts.Count);
        for (int i = 0; i < parts.Count; i++) {
            files.Add(new GeneratedSourceFile(SegmentFileName(segment, i + 1, parts.Count),
                SegmentFilePrefix(parts[i].Any(chunk => chunk.HasFrame)) +
                string.Concat(parts[i].Select(chunk => chunk.Text)) + "}\n"));
        }
        return files;
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
        writer.Line("using Spice86.Core.Emulator.Memory.Mmu;");
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
            if (registration.SingleEntry) {
                string delegateName = typedSignatures.DelegateNameOf(registration.MethodName);
                writer.Line($"DefineFunctionWithoutInterpreterFallback({registration.SegmentVariable}, 0x{registration.Offset:X4}, {GeneratedOverrideNames.EntryFieldNameOf(delegateName)}, \"{registration.BaseName}\");");
            } else if (registration.LoadOffset == 0) {
                writer.Line($"DefineFunctionWithoutInterpreterFallback({registration.SegmentVariable}, 0x{registration.Offset:X4}, {registration.MethodName}, \"{registration.BaseName}\");");
            } else {
                string entryName = BuildSecondaryEntryName(registration.BaseName);
                writer.Line($"DefineFunctionWithoutInterpreterFallback({registration.SegmentVariable}, 0x{registration.Offset:X4}, _ => {registration.MethodName}(0x{registration.LoadOffset:X4}), \"{entryName}\");");
            }
        }
        writer.CloseBlock();
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
