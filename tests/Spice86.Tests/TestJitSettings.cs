namespace Spice86.Tests;

/// <summary>JIT settings read from the environment, shared by every test that compiles CFG nodes.</summary>
internal static class TestJitSettings {
    /// <summary>Environment variable that turns <see cref="AluExpansion"/> on when set to <c>1</c>.</summary>
    public const string AluExpansionVariable = "SPICE86_JIT_ALU_EXPANSION";

    /// <summary>
    /// True when <see cref="AluExpansionVariable"/> is <c>1</c>: the JIT then executes the inline ALU expansion
    /// instead of the ALU calls.
    /// </summary>
    public static bool AluExpansion { get; } = Environment.GetEnvironmentVariable(AluExpansionVariable) == "1";
}
