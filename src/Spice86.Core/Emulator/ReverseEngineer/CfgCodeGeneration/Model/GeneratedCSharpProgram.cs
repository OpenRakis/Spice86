namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

internal sealed class GeneratedCSharpProgram {
    public required string SourceText { get; init; }
    /// <summary>
    /// The census of the <c>goto</c>s the printed program holds; empty for programs built without generation. A
    /// diagnostic of the generator, read by the <c>goto</c> census report of the generated-code tests.
    /// </summary>
    public GotoCensus GotoCensus { get; init; } = new();
    /// <summary>
    /// The census of the status-flag reads the printed program holds; empty for programs built without
    /// generation. A diagnostic of the generator, read by the flag-read census report of the generated-code tests.
    /// </summary>
    public FlagReadCensus FlagReadCensus { get; init; } = new();
    /// <summary>
    /// The census of the register webs of the printed program's methods; empty for programs built without
    /// generation. A diagnostic of the generator, read by the web census report of the generated-code tests.
    /// </summary>
    public WebCensus WebCensus { get; init; } = new();
}
