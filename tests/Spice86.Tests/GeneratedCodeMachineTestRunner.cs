namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.VM;

using Spice86.Tests.AsmFixtures;

using System.Runtime.CompilerServices;

using Xunit;

internal sealed class GeneratedCodeMachineTestRunner {
    /// <summary>
    /// Generates C# for one fixture, compiles it, re-runs the fixture as an override run and compares the
    /// generated source with its golden.
    /// </summary>
    /// <param name="fixture">Fixture used for discovery and for the override run.</param>
    /// <returns>The generated program.</returns>
    public GeneratedCSharpProgram TestGeneratedCode(AsmFixture fixture) {
        return TestGeneratedCode(fixture, [fixture]);
    }

    /// <summary>
    /// Generates C# from one discovery fixture, compiles it, then runs every execution fixture with the
    /// compiled override installed and checks that execution's oracle. The generated source is compared
    /// with its golden at the end.
    /// </summary>
    /// <param name="discovery">Fixture whose run is recorded for code generation.</param>
    /// <param name="executions">Fixtures run with the compiled override, in order, one fresh emulator each.</param>
    /// <returns>The generated program.</returns>
    /// <exception cref="ArgumentException">Discovery and execution do not share the same golden key.</exception>
    public GeneratedCSharpProgram TestGeneratedCode(AsmFixture discovery, IReadOnlyList<AsmFixture> executions) {
        string goldenKey = GoldenKey(discovery.Settings);
        foreach (AsmFixture execution in executions) {
            string executionKey = GoldenKey(execution.Settings);
            if (executionKey != goldenKey) {
                throw new ArgumentException(
                    $"Discovery golden key '{goldenKey}' differs from execution golden key '{executionKey}'.");
            }
        }

        (GeneratedCSharpProgram generatedProgram, AsmFinalState discoveryFinalState) =
            GenerateSourceAndFinalState(discovery.Settings);
        using CompiledGeneratedOverride compiledOverride = CompileGeneratedProgram(discovery.Settings, generatedProgram);
        foreach (AsmFixture execution in executions) {
            using AsmFixtureRun run = AsmFixtureRunner.RunWithOverride(execution, compiledOverride.Supplier);
            if (execution.Settings == discovery.Settings) {
                GeneratedCodeDifferentialReport.Append(goldenKey, discoveryFinalState, AsmFinalState.Capture(run.Result));
            }
        }

        // Compared last: a behavioural regression (memory dump or caller assertion) is the more
        // important failure and must be the one xunit reports when both a behavioural and a golden
        // mismatch occur on the same run.
        CompareGeneratedSourceWithExpected(goldenKey, generatedProgram);

        return generatedProgram;
    }

    /// <summary>
    /// Compiles an existing generated program and records its metrics.
    /// </summary>
    /// <param name="settings">The settings used for discovery and the golden key.</param>
    /// <param name="generatedProgram">The source produced by that discovery run.</param>
    /// <returns>The compiled override, which the caller must dispose.</returns>
    public CompiledGeneratedOverride CompileGeneratedProgram(AsmRunSettings settings, GeneratedCSharpProgram generatedProgram) {
        GeneratedOverrideCompiler compiler = new();
        GeneratedCompilation compilation = compiler.Compile(generatedProgram.SourceText);
        GeneratedCodeMetrics metrics = GeneratedCodeMetricsCollector.Collect(
            GoldenKey(settings), compilation.SyntaxTree, compilation.EmitResult.Diagnostics);
        WriteMetrics(metrics);
        GeneratedCodeMetricsAssertions.AssertInvariants(metrics, generatedProgram.SourceText);
        return compiler.CompileSupplier(compilation);
    }

    /// <summary>
    /// Builds the golden-file key for a run: the bin file name without extension, followed by one
    /// fixed suffix per enabled boolean flag of the <see cref="AsmRunSettings"/>, in a fixed
    /// order. Runs of the same bin with different options therefore get distinct goldens instead of
    /// overwriting each other. <c>MaxCycles</c> and <c>ConfigureMachine</c> are deliberately not part
    /// of the key. Every flag is enumerated explicitly so adding one to the settings is a
    /// compile-visible decision here rather than a silent omission that lets two runs share a golden.
    /// </summary>
    /// <param name="settings">Settings to build the key from.</param>
    /// <returns>The golden-file key.</returns>
    public static string GoldenKey(AsmRunSettings settings) {
        string key = Path.GetFileNameWithoutExtension(settings.BinName);
        if (settings.EnablePit) {
            key += ".pit";
        }
        if (settings.EnableA20Gate) {
            key += ".a20";
        }
        if (settings.InstallInterruptVectors) {
            key += ".ivt";
        }
        if (settings.FailOnUnhandledPort) {
            key += ".failport";
        }
        if (settings.EnableSpeculativeCfgExploration) {
            key += ".spec";
        }
        return key;
    }

    public GeneratedCSharpProgram GenerateProgramAndSource(AsmRunSettings settings) {
        (GeneratedCSharpProgram generatedProgram, _) = GenerateSourceAndFinalState(settings);
        return generatedProgram;
    }

    public void CompareGeneratedSourceWithExpected(string goldenKey, GeneratedCSharpProgram generatedProgram) {
        List<string> actualLines = NormalizeLines(generatedProgram.SourceText);
        //WriteExpectedGeneratedSource(goldenKey, generatedProgram.SourceText);
        string resPath = Path.Join("Resources", "cpuTests", "res", "GeneratedCode", goldenKey + ".cs.txt");
        File.Exists(resPath).Should().BeTrue(
            $"golden '{goldenKey}.cs.txt' is missing; regenerate it by uncommenting the WriteExpectedGeneratedSource call in GeneratedCodeMachineTestRunner.CompareGeneratedSourceWithExpected, running GeneratedCodeMachineTest and SpeculativeCfgTest once, then re-commenting it");
        List<string> expectedLines = NormalizeLines(File.ReadAllText(resPath));
        Assert.Equal(expectedLines, actualLines);
    }

    private static void WriteExpectedGeneratedSource(string goldenKey, string sourceText) {
        // Write directly to the source tree so the golden file is committed alongside the code.
        // CallerFilePath gives the location of this file in the source tree.
        string sourceDir = GetDirectoryName(GetSourceFilePath());
        string resPath = Path.Join(sourceDir, "Resources", "cpuTests", "res", "GeneratedCode", goldenKey + ".cs.txt");
        File.WriteAllText(resPath, sourceText);
    }

    private static List<string> NormalizeLines(string text) {
        return text.Replace("\r\n", "\n").Split('\n').ToList();
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;

    private static string GetDirectoryName(string path) {
        return Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"No directory for path: {path}");
    }

    private static void WriteGeneratedSource(string fileKey, GeneratedCSharpProgram generatedProgram) {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        Directory.CreateDirectory(outputDirectory);
        string fileName = fileKey;
        foreach (char invalidChar in Path.GetInvalidFileNameChars()) {
            fileName = fileName.Replace(invalidChar, '_');
        }
        File.WriteAllText(Path.Join(outputDirectory, fileName + ".generated.cs"), generatedProgram.SourceText);
    }

    private static readonly object MetricsFileLock = new();
    private static bool _metricsFileInitialized;

    private static void WriteMetrics(GeneratedCodeMetrics metrics) {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        Directory.CreateDirectory(outputDirectory);
        string metricsPath = Path.Join(outputDirectory, "metrics.txt");
        lock (MetricsFileLock) {
            if (!_metricsFileInitialized) {
                File.Delete(metricsPath);
                File.AppendAllText(metricsPath, GeneratedCodeMetrics.Header() + Environment.NewLine);
                _metricsFileInitialized = true;
            }
            File.AppendAllText(metricsPath, metrics.ToLine() + Environment.NewLine);
        }
    }

    private (GeneratedCSharpProgram GeneratedProgram, AsmFinalState FinalState) GenerateSourceAndFinalState(AsmRunSettings settings) {
        (CfgPartitionedProgram program, AsmFinalState finalState) = GenerateProgram(settings);
        GeneratedCSharpProgram generatedProgram = new CfgCSharpGenerator().Generate(program);
        WriteGeneratedSource(GoldenKey(settings), generatedProgram);
        return (generatedProgram, finalState);
    }

    private static (CfgPartitionedProgram Program, AsmFinalState FinalState) GenerateProgram(AsmRunSettings settings) {
        using AsmFixtureRun run = AsmFixtureRunner.RunSettings(settings, JitMode.InterpretedOnly);

        Machine machine = run.Machine;
        CfgBlockGraph graph = new CfgBlockGraphExporter().ExportFromExecutionContext(machine.CfgCpu.ExecutionContextManager, null).Graph;
        graph.Truncated.Should().BeFalse();

        CfgPartitionedProgram program = new CfgFunctionPartitioner().Partition(graph, machine.CfgCpu.ExecutionContextManager, new FunctionCatalogue());
        AsmFinalState finalState = AsmFinalState.Capture(run.Result);
        return (program, finalState);
    }
}
