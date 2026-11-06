namespace Spice86.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

/// <summary>
/// Result of parsing and emitting generated C# source: the parsed syntax tree, the emit result
/// (including diagnostics, which are always populated regardless of success), and the emitted PE stream.
/// </summary>
internal sealed class GeneratedCompilation {
    public GeneratedCompilation(IReadOnlyList<string> sources, CSharpCompilation compilation, EmitResult emitResult, MemoryStream peStream) {
        Source = string.Join(Environment.NewLine, sources);
        Compilation = compilation;
        SyntaxTrees = compilation.SyntaxTrees.ToArray();
        EmitResult = emitResult;
        PeStream = peStream;
    }

    public string Source { get; }
    public CSharpCompilation Compilation { get; }
    public IReadOnlyList<SyntaxTree> SyntaxTrees { get; }
    public EmitResult EmitResult { get; }
    public MemoryStream PeStream { get; }
}
