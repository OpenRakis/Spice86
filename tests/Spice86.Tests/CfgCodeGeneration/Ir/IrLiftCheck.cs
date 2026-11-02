namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Naming;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Collections.Concurrent;
using System.Diagnostics;

/// <summary>
/// The IR completeness check every generated-code test runs on its partitioned program: lifts every method,
/// verifies it with <see cref="IrVerifier"/>, and fails on any <see cref="OpaqueNode"/> statement.
/// <para>Set <c>SPICE86_IR_NODE_KINDS</c> to an absolute path to append one <c>kind&lt;TAB&gt;count</c> line
/// per statement node kind per lifted method; set <c>SPICE86_IR_DUMP</c> to an absolute path to append the
/// <see cref="IrDumper"/> dump of every lifted method.</para>
/// </summary>
internal static class IrLiftCheck {
    private static readonly ConcurrentDictionary<string, long> NodeKinds = new();
    private static readonly object FileLock = new();
    private static bool _kindsInitialized;
    private static bool _dumpInitialized;

    /// <summary>Lifts, rewrites and verifies every method of <paramref name="program"/>.</summary>
    public static void Check(CfgPartitionedProgram program, string fixture) {
        CfgCSharpGenerator generator = new();
        IReadOnlyList<LiftedMethod> methods = generator.LiftMethods(program);
        long ssaTicks = 0;
        foreach (LiftedMethod lifted in methods) {
            IrPassPipeline.Run(lifted.Method, IrPass.InstructionIdioms, verify: true);
            long started = Stopwatch.GetTimestamp();
            IrSsa ssa = IrSsa.Build(lifted.Method);
            IrVerifier.VerifySsa(lifted.Method, ssa);
            ssaTicks += Stopwatch.GetTimestamp() - started;
            CountKinds(lifted.Method);
        }
        AppendDump(program, methods);
        AppendKinds();
        AppendSsaTiming(fixture, ssaTicks);
    }

    private static void AppendSsaTiming(string fixture, long ssaTicks) {
        string? path = Environment.GetEnvironmentVariable("SPICE86_IR_SSA_TIMING");
        if (path == null) {
            return;
        }
        double milliseconds = ssaTicks * 1000.0 / Stopwatch.Frequency;
        lock (FileLock) {
            File.AppendAllLines(path, [$"{fixture}\t{milliseconds:F1}"]);
        }
    }

    private static void CountKinds(IrMethod method) {
        Dictionary<string, long> local = [];
        foreach (IrBlock block in method.Blocks) {
            foreach (IGrouping<string, IrStatement> group in block.Statements
                    .GroupBy(statement => statement.Node.GetType().Name)) {
                string kind = group.Key;
                int added = group.Count();
                local[kind] = local.GetValueOrDefault(kind) + added;
                NodeKinds.AddOrUpdate(kind, added, (_, count) => count + added);
            }
        }
    }

    private static void AppendKinds() {
        string? path = Environment.GetEnvironmentVariable("SPICE86_IR_NODE_KINDS");
        if (path == null) {
            return;
        }
        lock (FileLock) {
            if (!_kindsInitialized) {
                File.Delete(path);
                _kindsInitialized = true;
            }
            File.AppendAllLines(path,
                NodeKinds.Select(pair => $"{pair.Key}\t{pair.Value}"));
            NodeKinds.Clear();
        }
    }

    private static void AppendDump(CfgPartitionedProgram program, IReadOnlyList<LiftedMethod> methods) {
        string? path = Environment.GetEnvironmentVariable("SPICE86_IR_DUMP");
        if (path == null) {
            return;
        }
        AstInstructionRenderer renderer = new(AsmRenderingConfig.CreateSpice86Style());
        CfgGeneratorContext context = GeneratorAnalysis.Build(program, new InstructionMnemonic(renderer)).Context;
        lock (FileLock) {
            if (!_dumpInitialized) {
                File.Delete(path);
                _dumpInitialized = true;
            }
            File.AppendAllText(path,
                string.Concat(methods.Select(lifted => IrDumper.Dump(lifted.Method, context) + Environment.NewLine)));
        }
    }
}
