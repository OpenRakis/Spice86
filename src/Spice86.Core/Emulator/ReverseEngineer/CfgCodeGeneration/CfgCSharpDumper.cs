namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Interfaces;

using System.IO;

/// <summary>
/// Writes the generated C# override file to the recording directory. Thin adapter between the
/// state-serialization layer and the generator: receives a partitioned program and produces the source text.
/// </summary>
internal sealed class CfgCSharpDumper {
    private readonly CfgCSharpGenerator _generator;
    private readonly ILoggerService _loggerService;

    public CfgCSharpDumper(CfgCSharpGenerator generator, ILoggerService loggerService) {
        _generator = generator;
        _loggerService = loggerService;
    }

    public GeneratedCSharpProgram Generate(CfgPartitionedProgram program) {
        if (program.Partitions.Count == 0) {
            return new GeneratedCSharpProgram {
                SourceText = "// No CFG partitions were observed.\n"
            };
        }

        return _generator.Generate(program);
    }

    public void Write(CfgPartitionedProgram program, string path) {
        File.WriteAllText(path, Generate(program).SourceText);
    }

    /// <summary>
    /// Emits a self-contained, runnable project (csproj + Program.cs + the generated overrides) into
    /// <paramref name="projectDirectory"/>. Re-running overwrites the three files in place.
    /// An empty program (no partitions observed) emits nothing, mirroring the <see cref="Generate"/> guard.
    /// </summary>
    /// <param name="program">The partitioned CFG program to generate overrides for.</param>
    /// <param name="projectDirectory">The target project folder; created if missing.</param>
    /// <param name="expectedChecksum">Uppercase SHA-256 hex of the program, baked into Program.cs.</param>
    public void WriteProject(CfgPartitionedProgram program, string projectDirectory, string expectedChecksum) {
        if (program.Partitions.Count == 0) {
            return;
        }

        Directory.CreateDirectory(projectDirectory);

        string csProjPath = Path.Join(projectDirectory, GeneratedProjectScaffolder.CsProjFileName);
        string programPath = Path.Join(projectDirectory, GeneratedProjectScaffolder.ProgramFileName);
        string overridesPath = Path.Join(projectDirectory, GeneratedOverrideNames.DumpFileSuffix);

        File.WriteAllText(csProjPath, BuildCsProjAndPropsForCurrentInstall(projectDirectory));
        File.WriteAllText(programPath, GeneratedProjectScaffolder.BuildProgramCs(expectedChecksum));
        File.WriteAllText(overridesPath, Generate(program).SourceText);
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
