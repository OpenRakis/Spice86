namespace Spice86.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

/// <summary>
/// Result of parsing and emitting generated C# source: the parsed syntax tree, the emit result
/// (including diagnostics, which are always populated regardless of success), and the emitted PE stream.
/// </summary>
internal sealed class GeneratedCompilation {
    public GeneratedCompilation(string source, SyntaxTree syntaxTree, EmitResult emitResult, MemoryStream peStream) {
        Source = source;
        SyntaxTree = syntaxTree;
        EmitResult = emitResult;
        PeStream = peStream;
    }

    public string Source { get; }
    public SyntaxTree SyntaxTree { get; }
    public EmitResult EmitResult { get; }
    public MemoryStream PeStream { get; }
}
