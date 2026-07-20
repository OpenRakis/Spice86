namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Microsoft.Extensions.Logging;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

/// <summary>
/// The top-level orchestrator: takes a partitioned CFG program and produces a complete, compilable C# source
/// file. Runs the pipeline in order: analyze → plan → lower each method → render to text.
/// </summary>
internal sealed class CfgCSharpGenerator {
    private const string EntrySuffix = "_entry";

    private readonly AstInstructionRenderer _assemblyRenderer = new(AsmRenderingConfig.CreateSpice86Style());

    public GeneratedCSharpProgram Generate(CfgPartitionedProgram program) {
        if (program.Partitions.Count == 0) {
            throw new InvalidOperationException("Cannot generate C# for an empty CFG partitioned program.");
        }

        GeneratorAnalysis analysis = GeneratorAnalysis.Build(program);
        CfgGeneratorContext context = analysis.Context;
        GenerationPlan plan = GenerationPlanBuilder.Build(context);
        TransferEmitter transferEmitter = new(context);
        CSharpAstEmitter astEmitter = new(context, transferEmitter);
        CpuFaultWrapper cpuFaultWrapper = new(context, transferEmitter);
        MethodEmitter methodEmitter = new(context, cpuFaultWrapper, astEmitter, _assemblyRenderer);

        CSharpSourceWriter writer = new();
        EmitHeader(writer);
        writer.Line($"namespace {GeneratedOverrideNames.GeneratedNamespace};");
        writer.Line();
        EmitSupplier(writer, plan);
        writer.OpenBlock($"public class {GeneratedOverrideNames.OverrideClassName} : CSharpOverrideHelper");
        EmitSegmentFields(writer, plan);
        EmitConstructor(writer, plan);
        foreach (MethodPlan method in plan.Methods) {
            methodEmitter.Emit(writer, method);
        }
        writer.CloseBlock();

        return new GeneratedCSharpProgram {
            SourceText = writer.ToString()
        };
    }

    private static void EmitHeader(CSharpSourceWriter writer) {
        writer.Line("using Spice86.Core.CLI;");
        writer.Line("using Spice86.Core.Emulator.CPU.Exceptions;");
        writer.Line("using Spice86.Core.Emulator.Function;");
        writer.Line("using Spice86.Core.Emulator.ReverseEngineer;");
        writer.Line("using Spice86.Core.Emulator.VM;");
        writer.Line("using Spice86.Shared.Emulator.Memory;");
        writer.Line("using Spice86.Shared.Interfaces;");
        writer.Line("using System;");
        writer.Line("using System.Collections.Generic;");
        writer.Line();
    }

    private static void EmitSupplier(CSharpSourceWriter writer, GenerationPlan plan) {
        writer.OpenBlock($"public sealed class {GeneratedOverrideNames.SupplierClassName} : IOverrideSupplier");
        writer.OpenBlock("public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(ILoggerService loggerService, Configuration configuration, ushort programStartAddress, Machine machine)");
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

    private static void EmitConstructor(CSharpSourceWriter writer, GenerationPlan plan) {
        writer.OpenBlock($"public {GeneratedOverrideNames.OverrideClassName}(IDictionary<SegmentedAddress, FunctionInformation> functionInformations, Machine machine, ILoggerService loggerService, Configuration configuration, ushort programStartSegment) : base(functionInformations, machine, loggerService, configuration)");
        foreach (SegmentFieldPlan segment in plan.SegmentFields) {
            writer.Line($"{segment.FieldName} = 0x{segment.Segment:X4};");
        }
        writer.Line();
        foreach (OverrideRegistration registration in plan.OverrideRegistrations) {
            if (registration.LoadOffset == 0) {
                writer.Line($"DefineFunction({registration.SegmentVariable}, 0x{registration.Offset:X4}, {registration.MethodName});");
            } else {
                string entryName = BuildSecondaryEntryName(registration.BaseName);
                writer.Line($"DefineFunction({registration.SegmentVariable}, 0x{registration.Offset:X4}, _ => {registration.MethodName}(0x{registration.LoadOffset:X4}), name: \"{entryName}\");");
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
