namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Xunit;

/// <summary>
/// The directory and project writes of <see cref="CfgCSharpDumper"/>: every generated file lands under
/// its own name and the legacy single overrides file is removed from a generated project.
/// </summary>
public class CfgCSharpDumperTest {
    private static readonly GeneratedSourceFile SupplierFile =
        new("CfgGeneratedOverrideSupplier.cs", "// supplier\n");
    private static readonly GeneratedSourceFile MainFile =
        new("CfgGeneratedOverrides.cs", "// main overrides\n");
    private static readonly GeneratedSourceFile SegmentFile =
        new("CfgGeneratedOverrides.F000.cs", "// segment methods\n");

    private static CfgCSharpDumper NewDumper() => new(new CfgCSharpGenerator(), NullLogger.Instance);

    private static CfgPartitionedProgram NonEmptyProgram() => new() {
        Partitions = [new CfgCodePartition {
            Id = 1,
            Kind = CfgCodePartitionKind.Observed,
            Name = "partition1",
            Blocks = [],
            Entries = []
        }],
        Transfers = [],
        Inputs = GeneratorInputs.Empty
    };

    [Fact]
    public void WriteWritesEveryFileIntoTheDirectoryUnderItsName() {
        // Arrange
        string directory = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        GeneratedCSharpProgram program = new() { Files = [SupplierFile, MainFile, SegmentFile] };

        try {
            // Act
            NewDumper().Write(program, directory);

            // Assert
            foreach (GeneratedSourceFile file in program.Files) {
                File.ReadAllText(Path.Join(directory, file.Name)).Should().Be(file.Text);
            }
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteOfAnEmptyProgramWritesTheSingleCommentText() {
        // Arrange
        string directory = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        GeneratedCSharpProgram program = new() { Files = [] };

        try {
            // Act
            NewDumper().Write(program, directory);

            // Assert
            File.ReadAllText(Path.Join(directory, "CfgGeneratedOverrides.cs"))
                .Should().StartWith("// No CFG partitions were observed.");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteProjectDeletesTheLegacySingleOverridesFileAndWritesEveryFile() {
        // Arrange
        string directory = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string legacyPath = Path.Join(directory, "CfgGeneratedOverrides.cs");
        File.WriteAllText(legacyPath, "// whole program in one file, from an earlier scaffolder\n");
        GeneratedCSharpProgram program = new() { Files = [SupplierFile, MainFile, SegmentFile] };

        try {
            // Act
            NewDumper().WriteProject(NonEmptyProgram(), program, directory, "00");

            // Assert
            File.ReadAllText(legacyPath).Should().Be(MainFile.Text,
                "the legacy single file was replaced by the new main overrides file");
            foreach (GeneratedSourceFile file in program.Files) {
                File.ReadAllText(Path.Join(directory, file.Name)).Should().Be(file.Text);
            }
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SecondWriteLeavesNoFileOfTheFirstProgram() {
        // Arrange
        string directory = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        GeneratedCSharpProgram first = new() { Files = [SupplierFile, MainFile, SegmentFile] };
        GeneratedCSharpProgram second = new() { Files = [SupplierFile, MainFile] };
        CfgCSharpDumper dumper = NewDumper();

        try {
            dumper.Write(first, directory);

            // Act
            dumper.Write(second, directory);

            // Assert
            File.Exists(Path.Join(directory, SegmentFile.Name)).Should().BeFalse(
                "the second program has no F000 segment file");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SecondWriteProjectLeavesNoFileOfTheFirstProgram() {
        // Arrange
        string directory = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        GeneratedCSharpProgram first = new() { Files = [SupplierFile, MainFile, SegmentFile] };
        GeneratedCSharpProgram second = new() { Files = [SupplierFile, MainFile] };
        CfgCSharpDumper dumper = NewDumper();

        try {
            dumper.WriteProject(NonEmptyProgram(), first, directory, "00");

            // Act
            dumper.WriteProject(NonEmptyProgram(), second, directory, "00");

            // Assert
            File.Exists(Path.Join(directory, SegmentFile.Name)).Should().BeFalse(
                "the second program has no F000 segment file");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SecondWriteKeepsTheUserFiles() {
        // Arrange: after a first project write, the folder also holds a user file the generator
        // knows nothing of.
        string directory = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        GeneratedCSharpProgram program = new() { Files = [SupplierFile, MainFile] };
        CfgCSharpDumper dumper = NewDumper();

        try {
            dumper.WriteProject(NonEmptyProgram(), program, directory, "00");
            File.WriteAllText(Path.Join(directory, "Notes.cs"), "// user notes\n");

            // Act
            dumper.WriteProject(NonEmptyProgram(), program, directory, "00");

            // Assert
            File.Exists(Path.Join(directory, "Program.cs")).Should().BeTrue();
            File.Exists(Path.Join(directory, "Spice86.Generated.csproj")).Should().BeTrue();
            File.Exists(Path.Join(directory, "Notes.cs")).Should().BeTrue();
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }
}
