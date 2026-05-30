namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

/// <summary>
/// A braced block introduced by a header (e.g. <c>if (...)</c>, <c>else</c>, <c>while (...)</c>, <c>try</c>,
/// <c>catch (...)</c>). The body items render one indentation level deeper between <c>{</c> and <c>}</c>.
/// </summary>
internal sealed record BlockStatement(string Header, IReadOnlyList<StatementItem> Body) : StatementItem {
    // A bare block (try/catch/while/if-without-else) can always fall through: an `if` whose body diverges
    // still completes via the absent else, a `while` may run zero times, and a `try`/`catch` pair's
    // completion is driven by the statements the renderer places after it. Paired if/else divergence is
    // composed by the visitor as two BlockStatements; recognizing that pairing is intentionally out of
    // scope here, so a conservative `true` keeps the trailing throw whenever completion is uncertain.
    public override bool CompletesNormally => true;
}
