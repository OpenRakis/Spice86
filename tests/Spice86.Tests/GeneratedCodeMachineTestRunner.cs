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

using System.Runtime.CompilerServices;

using Xunit;

internal sealed class GeneratedCodeMachineTestRunner {
    public static byte[] GetExpectedMemoryDump(string binName) {
        return File.ReadAllBytes($"Resources/cpuTests/res/MemoryDumps/{binName}.bin");
    }

    public void TestGeneratedCode(string binName, long maxCycles = 100000) {
        string memoryDumpPath = $"Resources/cpuTests/res/MemoryDumps/{binName}.bin";
        byte[] expected = File.Exists(memoryDumpPath) ? File.ReadAllBytes(memoryDumpPath) : [];
        TestGeneratedCode(binName, expected, maxCycles);
    }

    public void TestGeneratedCode(string binName, byte[] expected, long maxCycles = 100000) {
        TestGeneratedCode(binName, expected, new GeneratedCodeRunOptions { MaxCycles = maxCycles });
    }

    public void TestGeneratedCode(string binName, byte[] expected, GeneratedCodeRunOptions options, Action<Machine>? assertions = null) {
        (CompiledGeneratedOverride compiledOverride, GeneratedCSharpProgram generatedProgram) = GenerateAndCompileSupplier(binName, options);
        using CompiledGeneratedOverride ownedOverride = compiledOverride;

        using Spice86Creator creator = new(binName: binName, maxCycles: options.MaxCycles, enablePit: options.EnablePit,
            installInterruptVectors: options.InstallInterruptVectors, failOnUnhandledPort: options.FailOnUnhandledPort,
            enableA20Gate: options.EnableA20Gate, jitMode: JitMode.InterpretedOnly, overrideSupplier: compiledOverride.Supplier,
            enableSpeculativeCfgExploration: options.EnableSpeculativeCfgExploration);
        using Spice86DependencyInjection spice86DependencyInjection = creator.Create();
        options.ConfigureMachine?.Invoke(spice86DependencyInjection.Machine);
        spice86DependencyInjection.FunctionCatalogue.FunctionInformations.Values
            .Should().Contain(functionInformation => functionInformation.HasOverride);
        spice86DependencyInjection.ProgramExecutor.Run();

        if (expected.Length != 0) {
            byte[] actual = spice86DependencyInjection.Machine.Memory.ReadRam((uint)expected.Length);
            actual.Should().Equal(expected);
        }
        assertions?.Invoke(spice86DependencyInjection.Machine);

        // Compared last: a behavioural regression (memory dump or caller assertion) is the more
        // important failure and must be the one xunit reports when both a behavioural and a golden
        // mismatch occur on the same run.
        CompareGeneratedSourceWithExpected(GoldenKey(binName, options), generatedProgram);
    }

    /// <summary>
    /// Builds the golden-file key for a run: the bin file name without extension, followed by one
    /// fixed suffix per enabled boolean flag in <see cref="GeneratedCodeRunOptions"/>, in a fixed
    /// order. Runs of the same bin with different options therefore get distinct goldens instead of
    /// overwriting each other. <c>MaxCycles</c> and <c>ConfigureMachine</c> are deliberately not part
    /// of the key. Every flag is enumerated explicitly so adding one to the options is a
    /// compile-visible decision here rather than a silent omission that lets two runs share a golden.
    /// </summary>
    public static string GoldenKey(string binName, GeneratedCodeRunOptions options) {
        string key = Path.GetFileNameWithoutExtension(binName);
        if (options.EnablePit) {
            key += ".pit";
        }
        if (options.EnableA20Gate) {
            key += ".a20";
        }
        if (options.InstallInterruptVectors) {
            key += ".ivt";
        }
        if (options.FailOnUnhandledPort) {
            key += ".failport";
        }
        if (options.EnableSpeculativeCfgExploration) {
            key += ".spec";
        }
        return key;
    }

    public (CfgPartitionedProgram Program, GeneratedCSharpProgram GeneratedProgram) GenerateProgramAndSource(string binName, long maxCycles) {
        return GenerateProgramAndSource(binName, new GeneratedCodeRunOptions { MaxCycles = maxCycles });
    }

    public (CfgPartitionedProgram Program, GeneratedCSharpProgram GeneratedProgram) GenerateProgramAndSource(string binName, long maxCycles, bool installInterruptVectors) {
        return GenerateProgramAndSource(binName, new GeneratedCodeRunOptions { MaxCycles = maxCycles, InstallInterruptVectors = installInterruptVectors });
    }

    public (CfgPartitionedProgram Program, GeneratedCSharpProgram GeneratedProgram) GenerateProgramAndSource(string binName, GeneratedCodeRunOptions options) {
        CfgPartitionedProgram program = GenerateProgram(binName, options);
        GeneratedCSharpProgram generatedProgram = new CfgCSharpGenerator().Generate(program);
        WriteGeneratedSource(binName, generatedProgram);
        return (program, generatedProgram);
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

    private static void WriteGeneratedSource(string binName, GeneratedCSharpProgram generatedProgram) {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        Directory.CreateDirectory(outputDirectory);
        string fileName = Path.GetFileNameWithoutExtension(binName);
        foreach (char invalidChar in Path.GetInvalidFileNameChars()) {
            fileName = fileName.Replace(invalidChar, '_');
        }
        File.WriteAllText(Path.Join(outputDirectory, fileName + ".generated.cs"), generatedProgram.SourceText);
    }

    private static (CompiledGeneratedOverride CompiledOverride, GeneratedCSharpProgram GeneratedProgram) GenerateAndCompileSupplier(string binName, GeneratedCodeRunOptions options) {
        (_, GeneratedCSharpProgram generatedProgram) = new GeneratedCodeMachineTestRunner().GenerateProgramAndSource(binName, options);

        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile(generatedProgram.SourceText);
        GeneratedCodeMetrics metrics = GeneratedCodeMetricsCollector.Collect(GoldenKey(binName, options), compilation.SyntaxTree, compilation.EmitResult.Diagnostics);
        WriteMetrics(metrics);
        return (new GeneratedOverrideCompiler().CompileSupplier(compilation), generatedProgram);
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

    private static CfgPartitionedProgram GenerateProgram(string binName, GeneratedCodeRunOptions options) {
        using Spice86Creator creator = new(binName: binName, maxCycles: options.MaxCycles, enablePit: options.EnablePit,
            installInterruptVectors: options.InstallInterruptVectors, failOnUnhandledPort: options.FailOnUnhandledPort,
            enableA20Gate: options.EnableA20Gate, jitMode: JitMode.InterpretedOnly,
            enableSpeculativeCfgExploration: options.EnableSpeculativeCfgExploration);
        using Spice86DependencyInjection spice86DependencyInjection = creator.Create();
        options.ConfigureMachine?.Invoke(spice86DependencyInjection.Machine);
        spice86DependencyInjection.ProgramExecutor.Run();

        Machine machine = spice86DependencyInjection.Machine;
        CfgBlockGraph graph = new CfgBlockGraphExporter().ExportFromExecutionContext(machine.CfgCpu.ExecutionContextManager, null).Graph;
        graph.Truncated.Should().BeFalse();

        return new CfgFunctionPartitioner().Partition(graph, machine.CfgCpu.ExecutionContextManager, new FunctionCatalogue());
    }
}
