namespace Spice86.Tests;

using FluentAssertions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using Spice86.Core.Emulator.Function;

using System.Reflection;
using System.Runtime.Loader;

internal sealed class GeneratedOverrideCompiler {
    public CompiledGeneratedOverride CompileSupplier(string source) {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "Spice86.GeneratedCode.Tests." + Guid.NewGuid().ToString("N"),
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream peStream = new();
        EmitResult emitResult = compilation.Emit(peStream);
        if (!emitResult.Success) {
            string diagnostics = string.Join(Environment.NewLine, emitResult.Diagnostics
                .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
                .Select(diagnostic => diagnostic.ToString()));
            emitResult.Success.Should().BeTrue("Generated source did not compile:" + Environment.NewLine + diagnostics + Environment.NewLine + source);
        }

        peStream.Position = 0;
        Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(peStream);
        Type supplierType = assembly.GetTypes().Single(type => typeof(IOverrideSupplier).IsAssignableFrom(type) && !type.IsAbstract);
        object supplierInstance = Activator.CreateInstance(supplierType)
            ?? throw new InvalidOperationException($"Could not instantiate generated supplier type {supplierType.FullName}.");
        return new CompiledGeneratedOverride((IOverrideSupplier)supplierInstance);
    }

    private static MetadataReference[] GetMetadataReferences() {
        string trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        IEnumerable<string> trustedPaths = trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<string> loadedPaths = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => assembly.Location);

        return trustedPaths.Concat(loadedPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
