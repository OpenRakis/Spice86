namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using System.IO;
using System.Reflection;

/// <summary>
/// Produces the scaffold files (csproj + Program.cs) that turn the generated overrides into a
/// self-contained, runnable C# project. Text production only: all IO/path decisions stay in the dumper/writer
/// layer. The names baked in come from <see cref="GeneratedOverrideNames"/> so the project, the assembly name,
/// and the assembly-qualified supplier name cannot drift out of sync.
/// </summary>
internal static class GeneratedProjectScaffolder {
    /// <summary>
    /// The assembly name of the generated project. Pinned equal to the generated namespace so the
    /// assembly-qualified supplier name (<see cref="AssemblyQualifiedSupplierName"/>) is deterministic.
    /// </summary>
    public const string ProjectAssemblyName = GeneratedOverrideNames.GeneratedNamespace;

    /// <summary>NuGet package id of the Spice86 GUI application, used for the package-reference fallback.</summary>
    public const string Spice86PackageId = "Spice86";

    /// <summary>The file name of the generated project file.</summary>
    public const string CsProjFileName = ProjectAssemblyName + ".csproj";

    /// <summary>The file name of the generated entry-point.</summary>
    public const string ProgramFileName = "Program.cs";

    /// <summary>
    /// Assembly-qualified name of the generated override supplier, passed to Spice86 as the
    /// <c>--OverrideSupplierClassName</c> default. Assembly-qualified (not a bare type name) so
    /// <c>Type.GetType</c> resolves it from the generated project's own assembly rather than only
    /// from Spice86.Core.
    /// </summary>
    public const string AssemblyQualifiedSupplierName =
        GeneratedOverrideNames.GeneratedNamespace + "." + GeneratedOverrideNames.SupplierClassName +
        ", " + ProjectAssemblyName;

    /// <summary>
    /// Builds the <c>Spice86.Generated.csproj</c> text using a <c>ProjectReference</c> to the live Spice86 GUI
    /// project. Preferred during development: the build always matches the running source tree and transitively
    /// pulls in Spice86.Core / Spice86.Shared / Spice86.Logging and the <c>Spice86.Program.Main</c> entry point.
    /// Only usable when generation runs from within the source tree (see <see cref="TryResolveSpice86CsprojPath"/>).
    /// </summary>
    /// <param name="spice86CsprojReferencePath">Path (relative or absolute) to <c>src/Spice86/Spice86.csproj</c>.</param>
    public static string BuildCsProjWithProjectReference(string spice86CsprojReferencePath) {
        // MSBuild accepts forward slashes on every platform; normalize so the reference is portable.
        string referencePath = spice86CsprojReferencePath.Replace('\\', '/');
        return BuildCsProj($"""<ProjectReference Include="{referencePath}" />""");
    }

    /// <summary>
    /// Builds the <c>Spice86.Generated.csproj</c> text using a <c>PackageReference</c> to the published
    /// <c>Spice86</c> NuGet package, pinned to the version of the running emulator. Used when generation does not
    /// run from the source tree (a published or NuGet-installed build), where no <c>Spice86.csproj</c> exists to
    /// reference. Restoring requires that the matching <c>Spice86</c> package version is available on a feed.
    /// </summary>
    /// <param name="spice86PackageVersion">Version of the running emulator, pinned as the package version.</param>
    public static string BuildCsProjWithPackageReference(string spice86PackageVersion) {
        return BuildCsProj($"""<PackageReference Include="{Spice86PackageId}" Version="{spice86PackageVersion}" />""");
    }

    private static string BuildCsProj(string referenceItem) {
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>{ProjectAssemblyName}</AssemblyName>
                <RootNamespace>{GeneratedOverrideNames.GeneratedNamespace}</RootNamespace>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
              </PropertyGroup>
              <ItemGroup>
                {referenceItem}
              </ItemGroup>
            </Project>

            """;
    }

    /// <summary>
    /// Builds the <c>Program.cs</c> entry point. It forwards all user args to <c>Spice86.Program.Main</c>,
    /// defaulting in the generated override supplier and code overrides only when the user did not specify them,
    /// and always enforcing the expected checksum so the overrides cannot run against a different binary.
    /// </summary>
    /// <param name="expectedChecksum">Uppercase SHA-256 hex of the program the overrides were generated from.</param>
    public static string BuildProgramCs(string expectedChecksum) {
        return $$"""
            // Generated by Spice86. Run with: dotnet run -- -e /path/to/PROGRAM.EXE
            using System.Collections.Generic;
            using System.Linq;

            const string OverrideSupplier = "{{AssemblyQualifiedSupplierName}}";
            const string ExpectedChecksum = "{{expectedChecksum}}";

            List<string> spice86Args = [.. args];

            if (!spice86Args.Contains("-o") && !spice86Args.Contains("--OverrideSupplierClassName")) {
                spice86Args.Add("--OverrideSupplierClassName");
                spice86Args.Add(OverrideSupplier);
            }
            if (!spice86Args.Contains("-u") && !spice86Args.Contains("--UseCodeOverride")) {
                spice86Args.Add("--UseCodeOverride");
                spice86Args.Add("true");
            }
            if (!spice86Args.Contains("-x") && !spice86Args.Contains("--ExpectedChecksum")) {
                spice86Args.Add("--ExpectedChecksum");
                spice86Args.Add(ExpectedChecksum);
            }

            Spice86.Program.Main([.. spice86Args]);

            """;
    }

    /// <summary>
    /// Tries to resolve the path to <c>src/Spice86/Spice86.csproj</c> by walking up from the running app's
    /// base directory until the GUI project is found. Succeeds only when generation runs from within the source
    /// tree; a published or NuGet-installed build has no such project, in which case the caller falls back to a
    /// package reference (<see cref="BuildCsProjWithPackageReference"/>).
    /// </summary>
    /// <param name="spice86CsprojPath">The resolved path when found; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> when the GUI project was located, otherwise <c>false</c>.</returns>
    public static bool TryResolveSpice86CsprojPath(out string? spice86CsprojPath) {
        spice86CsprojPath = null;
        // Base directory of the running app: <repo>/src/Spice86/bin/<cfg>/<tfm>/ during development.
        // AppContext.BaseDirectory is used instead of Assembly.Location because the latter returns an empty
        // string for assemblies embedded in a single-file app (IL3000).
        string appBaseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(appBaseDirectory)) {
            return false;
        }
        DirectoryInfo? dir = new(appBaseDirectory);
        while (dir is not null) {
            string candidate = Path.Join(dir.FullName, "Spice86", "Spice86.csproj");
            if (File.Exists(candidate)) {
                spice86CsprojPath = candidate;
                return true;
            }
            string srcCandidate = Path.Join(dir.FullName, "src", "Spice86", "Spice86.csproj");
            if (File.Exists(srcCandidate)) {
                spice86CsprojPath = srcCandidate;
                return true;
            }
            dir = dir.Parent;
        }
        return false;
    }

    /// <summary>
    /// Version of the running emulator, used to pin the <c>Spice86</c> package reference in the package-reference
    /// fallback. Prefers the informational version (the NuGet package version) and falls back to the assembly
    /// version when the informational attribute is absent.
    /// </summary>
    /// <exception cref="InvalidOperationException">When no version can be determined for the running assembly.</exception>
    public static string GetRunningSpice86Version() {
        Assembly coreAssembly = typeof(CSharpOverrideHelper).Assembly;
        string? informationalVersion =
            coreAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informationalVersion)) {
            // The informational version can carry build metadata after a '+'; strip it so the value is a valid
            // NuGet version (e.g. "14.3.1+abc123" -> "14.3.1").
            int plusIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            if (plusIndex >= 0) {
                return informationalVersion.Substring(0, plusIndex);
            }
            return informationalVersion;
        }
        Version? assemblyVersion = coreAssembly.GetName().Version;
        if (assemblyVersion is not null) {
            return assemblyVersion.ToString();
        }
        throw new InvalidOperationException(
            "Could not determine the version of the running Spice86.Core assembly for the package reference.");
    }
}
