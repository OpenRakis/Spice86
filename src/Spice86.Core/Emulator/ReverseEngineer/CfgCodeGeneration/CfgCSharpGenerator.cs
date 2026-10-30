namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Microsoft.Extensions.Logging.Abstractions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Naming;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Shared.Interfaces;

using System.Linq;

/// <summary>
/// The top-level orchestrator: takes a partitioned CFG program and produces a complete, compilable C# source
/// file. Runs the pipeline in order: analyze → plan → lower each method → render to text.
/// </summary>
internal sealed class CfgCSharpGenerator {
    private const string EntrySuffix = "_entry";
    private const long SlowMethodThresholdMilliseconds = 1000;

    private readonly AstInstructionRenderer _assemblyRenderer = new(AsmRenderingConfig.CreateSpice86Style());
    private readonly bool _collectCensuses;
    private readonly bool _verify;

    /// <summary>The production generator: no census collection, no IR verification.</summary>
    public CfgCSharpGenerator() : this(false, false) {
    }

    /// <summary>Creates a generator; production uses the parameterless options.</summary>
    /// <param name="collectCensuses">Whether each emitted method runs the census classifiers the
    /// generated-program diagnostics read; production skips them.</param>
    /// <param name="verify">Whether the IR invariants are checked after the passes; tests enable it.
    /// </param>
    public CfgCSharpGenerator(bool collectCensuses, bool verify) {
        _collectCensuses = collectCensuses;
        _verify = verify;
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
        GeneratorAnalysis analysis = GeneratorAnalysis.Build(program, instructionMnemonic);
        progress.EndPhase();

        progress.BeginPhase(DumpPhase.CodePlan);
        CfgGeneratorContext context = analysis.Context;
        GenerationPlan plan = GenerationPlanBuilder.Build(context);
        TransferEmitter transferEmitter = new(context);
        InstructionLowerer lowerer = new(context, transferEmitter);
        CpuFaultWrapper cpuFaultWrapper = new(context, transferEmitter);
        IrMethodEmitter methodEmitter = new(context, cpuFaultWrapper, lowerer, _assemblyRenderer,
            _collectCensuses, _verify);

        CSharpSourceWriter writer = new();
        EmitHeader(writer);
        writer.Line($"namespace {GeneratedOverrideNames.GeneratedNamespace};");
        writer.Line();
        EmitSupplier(writer, plan);
        writer.OpenBlock($"public class {GeneratedOverrideNames.OverrideClassName} : CSharpOverrideHelper");
        EmitSegmentFields(writer, plan);
        EmitSignatureFields(writer, plan);
        EmitConstructor(writer, plan);
        progress.EndPhase();

        progress.BeginPhase(DumpPhase.CodeMethods);
        GotoCensus census = new();
        FlagReadCensus flagReadCensus = new();
        WebCensus webCensus = new();
        for (int i = 0; i < plan.Methods.Count; i++) {
            MethodPlan method = plan.Methods[i];
            progress.Step(i + 1, plan.Methods.Count, method.MethodName);
            GenerationStepTimer timer = new();
            MethodCensus methodCensus = methodEmitter.Emit(writer, method, timer);
            census.Merge(methodCensus.Gotos);
            flagReadCensus.Merge(methodCensus.FlagReads);
            webCensus.Merge(methodCensus.Webs);
            if (timer.TotalMilliseconds >= SlowMethodThresholdMilliseconds) {
                progress.Detail(SlowMethodLine(i + 1, plan.Methods.Count, method.MethodName,
                    method.Nodes.OfType<CfgInstruction>().Count(),
                    methodCensus.LiftedBlocks, methodCensus.LiftedStatements, timer));
            }
        }
        writer.CloseBlock();
        progress.EndPhase();

        return new GeneratedCSharpProgram {
            SourceText = writer.ToString(),
            GotoCensus = census,
            FlagReadCensus = flagReadCensus,
            WebCensus = webCensus
        };
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
        InstructionLowerer lowerer = new(context, transferEmitter);
        IrLifter lifter = new(context, lowerer, _assemblyRenderer);
        return plan.Methods.Select(method => new LiftedMethod(lifter.Lift(method), method, lowerer, context)).ToList();
    }

    private static void EmitHeader(CSharpSourceWriter writer) {
        writer.Line("using Spice86.Core.CLI;");
        writer.Line("using Spice86.Core.Emulator.CPU.Exceptions;");
        writer.Line("using Spice86.Core.Emulator.Function;");
        writer.Line("using Spice86.Core.Emulator.ReverseEngineer;");
        writer.Line("using Spice86.Core.Emulator.VM;");
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
    private static void EmitSignatureFields(CSharpSourceWriter writer, GenerationPlan plan) {
        if (plan.SignatureFields.Count == 0) {
            return;
        }

        foreach (SignatureFieldPlan field in plan.SignatureFields) {
            string bytes = string.Join(", ", field.Bytes.Select(value => value is byte byteValue ? $"0x{byteValue:X2}" : "null"));
            writer.Line($"private static readonly byte?[] {field.FieldName} = [{bytes}];");
        }
        writer.Line();
    }

    private static void EmitConstructor(CSharpSourceWriter writer, GenerationPlan plan) {
        writer.OpenBlock($"public {GeneratedOverrideNames.OverrideClassName}(IDictionary<SegmentedAddress, FunctionInformation> functionInformations, Machine machine, ILogger loggerService, Configuration configuration, ushort programStartSegment) : base(functionInformations, machine, loggerService, configuration)");
        foreach (SegmentFieldPlan segment in plan.SegmentFields) {
            writer.Line($"{segment.FieldName} = 0x{segment.Segment:X4};");
        }
        writer.Line();
        foreach (OverrideRegistration registration in plan.OverrideRegistrations) {
            if (registration.LoadOffset == 0) {
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
