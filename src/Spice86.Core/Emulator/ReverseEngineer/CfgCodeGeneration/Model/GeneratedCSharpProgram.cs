namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

internal sealed class GeneratedCSharpProgram {
    public required string SourceText { get; init; }
    /// <summary>
    /// The census of the <c>goto</c>s the printed program holds; empty for programs built without generation. A
    /// diagnostic of the generator, read by the <c>goto</c> census report of the generated-code tests.
    /// </summary>
    public GotoCensus GotoCensus { get; init; } = new();
}
