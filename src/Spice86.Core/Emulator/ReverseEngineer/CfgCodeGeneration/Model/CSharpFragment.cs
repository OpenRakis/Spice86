namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>
/// A piece of generated C# emitted by <see cref="CSharpAstEmitter"/> for an execution-AST node. It carries
/// the rendered text plus the context needed to avoid redundant casts and parentheses:
/// <list type="bullet">
/// <item><see cref="Type"/>: the C# data type the expression evaluates to, or <c>null</c> when unknown
/// (e.g. the <c>int</c>-promoted result of binary arithmetic). A cast to a type the fragment already has is
/// skipped.</item>
/// <item><see cref="Precedence"/>: the binding strength of the top-level operator, used to drop parentheses
/// that are not required by C# precedence.</item>
/// </list>
/// Implicit conversions to/from <see cref="string"/> keep interpolation transparent; a raw string is treated
/// as an atomic expression of unknown type.
/// </summary>
internal readonly record struct CSharpFragment(string Text, DataType? Type, int Precedence) {
    /// <summary>Precedence of a primary expression (literal, identifier, call, indexer, cast, parenthesized).</summary>
    public const int AtomicPrecedence = 13;

    public CSharpFragment(string Text) : this(Text, null, AtomicPrecedence) {
    }

    public static implicit operator CSharpFragment(string text) => new(text);
    public static implicit operator string(CSharpFragment fragment) => fragment.Text;
    public override string ToString() => Text;
}
