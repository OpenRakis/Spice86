using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;


using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Shared.Interfaces;

using System.IO;

/// <summary>
/// Writes the generated C# override file to the recording directory. Thin adapter between the
/// state-serialization layer and the generator: receives a partitioned program and produces the source text.
/// </summary>
internal sealed class CfgCSharpDumper {
    private readonly CfgCSharpGenerator _generator;
    private readonly ILogger _loggerService;

    public CfgCSharpDumper(CfgCSharpGenerator generator, ILogger loggerService) {
        _generator = generator;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Generates the override source for <paramref name="program"/>, reporting analysis, planning and
    /// per-method progress through <paramref name="progress"/>.
    /// </summary>
    public GeneratedCSharpProgram Generate(CfgPartitionedProgram program, DumpProgressReporter progress) {
        if (program.Partitions.Count == 0) {
            return new GeneratedCSharpProgram {
                SourceText = "// No CFG partitions were observed.\n"
            };
        }

        return _generator.Generate(program, progress);
    }

    /// <summary>
    /// Writes <paramref name="generated"/>'s source text to <paramref name="path"/>.
    /// </summary>
    /// <param name="generated">The generated program, from <see cref="Generate"/>.</param>
    /// <param name="path">The target file path.</param>
    public void Write(GeneratedCSharpProgram generated, string path) {
        File.WriteAllText(path, generated.SourceText);
    }

    /// <summary>
    /// Emits a self-contained, runnable project (csproj + Program.cs + <paramref name="generated"/>'s source
    /// text as the overrides file) into <paramref name="projectDirectory"/>. Re-running overwrites the files in
    /// place. An empty program (no partitions observed) emits nothing, mirroring the <see cref="Generate"/> guard.
    /// </summary>
    /// <param name="program">The partitioned CFG program the overrides were generated for.</param>
    /// <param name="generated">The generated program, from <see cref="Generate"/>.</param>
    /// <param name="projectDirectory">The target project folder; created if missing.</param>
    /// <param name="expectedChecksum">Uppercase SHA-256 hex of the program, baked into Program.cs.</param>
    public void WriteProject(CfgPartitionedProgram program, GeneratedCSharpProgram generated, string projectDirectory, string expectedChecksum) {
        if (program.Partitions.Count == 0) {
            return;
        }

        Directory.CreateDirectory(projectDirectory);

        string csProjPath = Path.Join(projectDirectory, GeneratedProjectScaffolder.CsProjFileName);
        string programPath = Path.Join(projectDirectory, GeneratedProjectScaffolder.ProgramFileName);
        string overridesPath = Path.Join(projectDirectory, GeneratedOverrideNames.DumpFileSuffix);

        File.WriteAllText(csProjPath, BuildCsProjAndPropsForCurrentInstall(projectDirectory));
        File.WriteAllText(programPath, GeneratedProjectScaffolder.BuildProgramCs(expectedChecksum));
        File.WriteAllText(overridesPath, generated.SourceText);
    }

    /// <summary>
    /// Picks the csproj reference for the running install and builds the project text accordingly. When the
    /// Spice86 source tree is found, a <c>ProjectReference</c> is used so the overrides build against the exact
    /// source being run (development), and a sibling <c>Directory.Build.props</c> is written pointing central
    /// package management at the source tree's <c>Directory.Packages.props</c> so restored package versions match
    /// the ones the already-built Spice86.Core.dll/Spice86.dll were compiled against. Otherwise a
    /// <c>PackageReference</c> pinned to the running version is used (release / NuGet install). The chosen
    /// reference is logged so users can diagnose build/restore problems.
    /// </summary>
    private string BuildCsProjAndPropsForCurrentInstall(string projectDirectory) {
        if (GeneratedProjectScaffolder.TryResolveSpice86CsprojPath(out string? spice86CsprojPath)
            && spice86CsprojPath is not null) {
            // Relative reference keeps the generated project portable when the whole tree is moved together.
            string referencePath = Path.GetRelativePath(projectDirectory, spice86CsprojPath);
            if (_loggerService.IsEnabled(LogLevel.Information)) {
                _loggerService.LogInformation(
                    "Generated project references the Spice86 source tree via ProjectReference to {Spice86CsprojPath}. " +
                    "This is expected for a development build run from the source tree.",
                    spice86CsprojPath);
            }

            string? directoryBuildProps = GeneratedProjectScaffolder.BuildDirectoryBuildPropsForProjectReference(referencePath);
            if (directoryBuildProps is not null) {
                string directoryBuildPropsPath = Path.Join(projectDirectory, GeneratedProjectScaffolder.DirectoryBuildPropsFileName);
                File.WriteAllText(directoryBuildPropsPath, directoryBuildProps);
            }

            return GeneratedProjectScaffolder.BuildCsProjWithProjectReference(referencePath);
        }

        string version = GeneratedProjectScaffolder.GetRunningSpice86Version();
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation(
                "Spice86 source tree not found next to the running assembly; generated project references the " +
                "{PackageId} NuGet package version {Version} via PackageReference. Restoring it requires that this " +
                "package version is available on a configured NuGet feed.",
                GeneratedProjectScaffolder.Spice86PackageId, version);
        }
        return GeneratedProjectScaffolder.BuildCsProjWithPackageReference(version);
    }
}

