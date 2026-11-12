namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Tests.Utility;

using Xunit;

/// <summary>Tests of the report row built by <see cref="GeneratedCodeCoverageReport"/> for one run.</summary>
public sealed class GeneratedCodeCoverageReportTest {
    /// <summary>A run that wrote no generated C# dump reports status no-generated-code.</summary>
    [Fact]
    public void MissingGeneratedFileIsReportedAsNoGeneratedCode() {
        // Arrange
        using TempFile folder = new("coverage");

        // Act
        string row = GeneratedCodeCoverageReport.BuildRow("fact", "/x/prog.com", folder.Path);

        // Assert
        string[] columns = row.Split('\t');
        columns[0].Should().Be("fact");
        columns[1].Should().Be("prog.com");
        columns[2].Should().Be("no-generated-code");
        columns[3].Should().BeEmpty();
        columns[4].Should().BeEmpty();
        columns[5].Should().BeEmpty();
    }

    /// <summary>A generated C# dump holding no partition reports status empty.</summary>
    [Fact]
    public void EmptyProgramIsReportedAsEmpty() {
        // Arrange
        using TempFile folder = new("coverage");
        string exportFolder = folder.CreateDirectory("hash");
        string generatedFile = Path.Join(
            CreateGeneratedDirectory(exportFolder), "CfgGeneratedOverrides.cs");
        File.WriteAllText(generatedFile, "// No CFG partitions were observed.\n");

        // Act
        string row = GeneratedCodeCoverageReport.BuildRow("fact", "/x/prog.com", folder.Path);

        // Assert
        string[] columns = row.Split('\t');
        columns[2].Should().Be("empty");
        columns[3].Should().BeEmpty();
        columns[4].Should().BeEmpty();
        columns[5].Should().BeEmpty();
    }

    /// <summary>A generated C# dump that compiles reports status ok with its method and line counts.</summary>
    [Fact]
    public void CompilableProgramIsReportedAsOk() {
        // Arrange
        using TempFile folder = new("coverage");
        string exportFolder = folder.CreateDirectory("hash");
        string generatedDirectory = CreateGeneratedDirectory(exportFolder);
        string source = File.ReadAllText(Path.Join("Resources", "cpuTests", "res", "GeneratedCode", "add.spec.cs.txt"));
        WriteGoldenFiles(generatedDirectory, source);

        // Act
        string row = GeneratedCodeCoverageReport.BuildRow("fact", "/x/prog.com", folder.Path);

        // Assert
        string[] columns = row.Split('\t');
        columns[2].Should().Be("ok");
        columns[3].Should().BeEmpty();
        columns[4].Should().Be("2");
        columns[5].Should().NotBeEmpty();
    }

    /// <summary>A generated C# dump that does not compile reports status compile-error with its error ids.</summary>
    [Fact]
    public void BrokenProgramIsReportedWithErrorIds() {
        // Arrange
        using TempFile folder = new("coverage");
        string exportFolder = folder.CreateDirectory("hash");
        string generatedFile = Path.Join(
            CreateGeneratedDirectory(exportFolder), "CfgGeneratedOverrides.cs");
        File.WriteAllText(generatedFile, "public class Broken {");

        // Act
        string row = GeneratedCodeCoverageReport.BuildRow("fact", "/x/prog.com", folder.Path);

        // Assert
        string[] columns = row.Split('\t');
        columns[2].Should().Be("compile-error");
        columns[3].Should().Contain("CS1513");
    }

    private static string CreateGeneratedDirectory(string exportFolder) =>
        Directory.CreateDirectory(Path.Join(exportFolder, GeneratedCodeCoverageReport.GeneratedDirectoryName)).FullName;

    /// <summary>Writes the files of a golden text (each section behind its
    /// <c>// ==== file: &lt;name&gt; ====</c> marker) into <paramref name="directory"/>.</summary>
    private static void WriteGoldenFiles(string directory, string goldenText) {
        string[] lines = goldenText.Split('\n');
        string? name = null;
        List<string> content = [];
        void Flush() {
            if (name is null) {
                return;
            }
            File.WriteAllText(Path.Join(directory, name), string.Join('\n', content));
            content.Clear();
        }
        foreach (string line in lines) {
            const string marker = "// ==== file: ";
            if (line.StartsWith(marker, StringComparison.Ordinal)) {
                Flush();
                name = line[marker.Length..].TrimEnd().TrimEnd('=').TrimEnd();
            } else if (name is not null) {
                content.Add(line);
            }
        }
        if (name is null) {
            File.WriteAllText(Path.Join(directory, "CfgGeneratedOverrides.cs"), goldenText);
            return;
        }
        Flush();
    }
}
