namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Microsoft.CodeAnalysis.CSharp;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The multi-file output of <see cref="CfgCSharpGenerator"/>: file list, file order, per-file
/// compilability and the segment-file line-limit split.
/// </summary>
public class CfgCSharpGeneratorFilesTest {
    private static CfgBlock RetBlock(TestInstructionHelper helper, ushort segment, ushort offset, int id) {
        CfgInstruction ret = helper.WriteAndParse(new SegmentedAddress(segment, offset),
            writer => writer.WriteBytes([0xC3]));
        return Block(id, ret);
    }

    private static CfgCodePartition PartitionOf(int id, ushort segment, CfgBlock block) =>
        new() {
            Id = id,
            Kind = CfgCodePartitionKind.Observed,
            Name = $"partition{id}",
            Blocks = [block],
            Entries = [new CfgCodePartitionEntry {
                Node = block.Entry,
                Kind = CfgCodePartitionEntryKind.FunctionEntry
            }]
        };

    private static GeneratedCSharpProgram Generate(params (ushort Segment, int Id)[] methods) {
        TestInstructionHelper helper = new();
        CfgCodePartition[] partitions = methods
            .Select(method => PartitionOf(method.Id, method.Segment, RetBlock(helper, method.Segment, 0x0000, method.Id)))
            .ToArray();
        return new CfgCSharpGenerator(collectCensuses: false, verify: false)
            .Generate(new CfgPartitionedProgram {
                Partitions = partitions, Transfers = [], Inputs = GeneratorInputs.Empty
            });
    }

    [Fact]
    public void EveryFileParsesAloneAndTheSetCompiles() {
        // Arrange
        GeneratedCSharpProgram program = Generate((0xF000, 1), (0xD000, 2));

        // Assert
        foreach (GeneratedSourceFile file in program.Files) {
            CSharpSyntaxTree.ParseText(file.Text, cancellationToken: TestContext.Current.CancellationToken)
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Should().BeEmpty($"file {file.Name} parses alone");
        }
        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile(program);
        compilation.EmitResult.Diagnostics
            .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated file set compiles");
    }

    [Fact]
    public void SegmentAboveTheLineLimitSplitsAtMethodBoundaries() {
        // Arrange: a limit a single method already exceeds forces one method per file.
        TestInstructionHelper helper = new();
        CfgCodePartition[] partitions = Enumerable.Range(1, 2)
            .Select(id => PartitionOf(id, 0xF000, RetBlock(helper, 0xF000, (ushort)(0x0000 + id * 0x10), id)))
            .ToArray();
        CfgCSharpGenerator generator = new(collectCensuses: false, verify: false, maxLinesPerFile: 1);

        // Act
        GeneratedCSharpProgram program = generator.Generate(
            new CfgPartitionedProgram {
                Partitions = partitions, Transfers = [], Inputs = GeneratorInputs.Empty
            });

        // Assert
        List<string> segmentFiles = program.Files
            .Select(file => file.Name)
            .Where(name => name.StartsWith("CfgGeneratedOverrides.F000"))
            .ToList();
        segmentFiles.Should().Equal("CfgGeneratedOverrides.F000.1.cs", "CfgGeneratedOverrides.F000.2.cs");
        foreach (GeneratedSourceFile file in program.Files.Where(file => segmentFiles.Contains(file.Name))) {
            file.Text.Should().Contain("public partial class CfgGeneratedOverrides");
            file.Text.Split("public virtual Action", StringSplitOptions.None).Length.Should().Be(2,
                "each part holds exactly one method");
        }
        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile(program);
        compilation.EmitResult.Success.Should().BeTrue("the split file set compiles");
    }

    [Fact]
    public void SegmentFieldsGetANameAndAnInitializerByRole() {
        // Arrange: entry 0x170, a 0x100-byte image covering 0x160..0x17F, code inside it at 0x160,
        // 0x170 and 0x175, and code outside it at the two BIOS segments and at 0x9000.
        TestInstructionHelper helper = new();
        CfgCodePartition[] partitions = new[] { (ushort)0x160, (ushort)0x170, (ushort)0x175,
                (ushort)0x9000, (ushort)0xC000, (ushort)0xF000 }
            .Select((segment, index) => PartitionOf(index + 1, segment, RetBlock(helper, segment, 0x0000, index + 1)))
            .ToArray();
        CfgPartitionedProgram partitioned = new() {
            Partitions = partitions,
            Transfers = [],
            Inputs = GeneratorInputs.Empty with {
                ProgramImages = ProgramImages.Create(0x170,
                    [new LoadedImage(LoadedImageKind.DosProgram, "program.com", 0x160, 0x1700, 0x100)])
            }
        };

        // Act
        GeneratedCSharpProgram program = new CfgCSharpGenerator(collectCensuses: false, verify: false)
            .Generate(partitioned);

        // Assert
        program.SourceText.Should().Contain("cs1 = (ushort)(programStartSegment - 0x0010);");
        program.SourceText.Should().Contain("cs2 = programStartSegment;");
        program.SourceText.Should().Contain("cs3 = (ushort)(programStartSegment + 0x0005);");
        program.SourceText.Should().Contain("cs4 = 0x9000;");
        program.SourceText.Should().Contain("videoBiosSegment = 0xC000;");
        program.SourceText.Should().Contain("biosSegment = 0xF000;");
    }

    [Fact]
    public void NoProgramImageKeepsObservedSegmentValuesButNamesTheBiosSegments() {
        // Arrange: the same segments, no image information.
        TestInstructionHelper helper = new();
        CfgCodePartition[] partitions = new[] { (ushort)0x160, (ushort)0x170,
                (ushort)0x9000, (ushort)0xC000, (ushort)0xF000 }
            .Select((segment, index) => PartitionOf(index + 1, segment, RetBlock(helper, segment, 0x0000, index + 1)))
            .ToArray();
        CfgPartitionedProgram partitioned = new() {
            Partitions = partitions, Transfers = [], Inputs = GeneratorInputs.Empty
        };

        // Act
        GeneratedCSharpProgram program = new CfgCSharpGenerator(collectCensuses: false, verify: false)
            .Generate(partitioned);

        // Assert
        program.SourceText.Should().Contain("cs1 = 0x0160;");
        program.SourceText.Should().Contain("cs2 = 0x0170;");
        program.SourceText.Should().Contain("cs3 = 0x9000;");
        program.SourceText.Should().Contain("videoBiosSegment = 0xC000;");
        program.SourceText.Should().Contain("biosSegment = 0xF000;");
        program.SourceText.Should().NotContain("programStartSegment - 0x");
    }

}
