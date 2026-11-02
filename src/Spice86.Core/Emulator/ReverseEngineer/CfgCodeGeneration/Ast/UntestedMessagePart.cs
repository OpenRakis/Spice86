namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>A literal text part or a value hole of a <see cref="FailAsUntestedNode"/> message.</summary>
/// <param name="Text">The literal text; empty for a hole.</param>
/// <param name="Value">The hole expression; <c>null</c> for a literal part.</param>
/// <param name="Format">The <c>string.Format</c> specifier a hole prints with; empty for none.</param>
internal sealed record UntestedMessagePart(string Text, ValueNode? Value, string Format) {
    /// <summary>Builds a literal text part.</summary>
    public static UntestedMessagePart Literal(string text) => new(text, null, string.Empty);

    /// <summary>Builds a value hole with a format specifier.</summary>
    public static UntestedMessagePart Hole(ValueNode value, string format) => new(string.Empty, value, format);
}
