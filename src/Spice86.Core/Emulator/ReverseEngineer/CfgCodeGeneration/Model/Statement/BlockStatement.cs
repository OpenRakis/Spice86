namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

/// <summary>
/// A braced block introduced by a header (e.g. <c>while (...)</c>, or an <c>if (...)</c> that has no <c>else</c>). The body items render one indentation level deeper between <c>{</c> and <c>}</c>.
/// </summary>
internal sealed record BlockStatement(string Header, IReadOnlyList<StatementItem> Body) : StatementItem {
    // A bare block (while, if-without-else) can always fall through: an `if` whose body diverges still
    // completes via the absent else, and a `while` may run zero times. Paired if/else and try/catch are
    // IfElseStatement and TryCatchStatement, which compute completion from both arms.
    public override bool CompletesNormally => true;
    public override IEnumerable<IReadOnlyList<StatementItem>> NestedBodies => [Body];
}
