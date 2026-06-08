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
        CompiledGeneratedOverride compiledOverride = GenerateAndCompileSupplier(binName, options);

        using Spice86Creator creator = new(binName: binName, maxCycles: options.MaxCycles, enablePit: options.EnablePit,
            installInterruptVectors: options.InstallInterruptVectors, failOnUnhandledPort: options.FailOnUnhandledPort,
            enableA20Gate: options.EnableA20Gate, jitMode: JitMode.InterpretedOnly, overrideSupplier: compiledOverride.Supplier);
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

    private static void WriteGeneratedSource(string binName, GeneratedCSharpProgram generatedProgram) {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        Directory.CreateDirectory(outputDirectory);
        string fileName = Path.GetFileNameWithoutExtension(binName);
        foreach (char invalidChar in Path.GetInvalidFileNameChars()) {
            fileName = fileName.Replace(invalidChar, '_');
        }
        File.WriteAllText(Path.Join(outputDirectory, fileName + ".generated.cs"), generatedProgram.SourceText);
    }

    private static CompiledGeneratedOverride GenerateAndCompileSupplier(string binName, GeneratedCodeRunOptions options) {
        (_, GeneratedCSharpProgram generatedProgram) = new GeneratedCodeMachineTestRunner().GenerateProgramAndSource(binName, options);

        return new GeneratedOverrideCompiler().CompileSupplier(generatedProgram.SourceText);
    }

    private static CfgPartitionedProgram GenerateProgram(string binName, GeneratedCodeRunOptions options) {
        using Spice86Creator creator = new(binName: binName, maxCycles: options.MaxCycles, enablePit: options.EnablePit,
            installInterruptVectors: options.InstallInterruptVectors, failOnUnhandledPort: options.FailOnUnhandledPort,
            enableA20Gate: options.EnableA20Gate, jitMode: JitMode.InterpretedOnly);
        using Spice86DependencyInjection spice86DependencyInjection = creator.Create();
        options.ConfigureMachine?.Invoke(spice86DependencyInjection.Machine);
        spice86DependencyInjection.ProgramExecutor.Run();

        Machine machine = spice86DependencyInjection.Machine;
        CfgBlockGraph graph = new CfgBlockGraphExporter().ExportFromExecutionContext(machine.CfgCpu.ExecutionContextManager, null).Graph;
        graph.Truncated.Should().BeFalse();

        return new CfgFunctionPartitioner().Partition(graph, machine.CfgCpu.ExecutionContextManager, new FunctionCatalogue());
    }
}
