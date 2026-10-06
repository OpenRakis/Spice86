namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

/// <summary>
/// An <c>if</c> with an optional <c>else</c>. <paramref name="Condition"/> is the C# condition without parentheses; the <c>else</c> is rendered only when <paramref name="FalseBody"/> is not empty.
/// </summary>
internal sealed record IfElseStatement(string Condition, IReadOnlyList<StatementItem> TrueBody, IReadOnlyList<StatementItem> FalseBody) : StatementItem {
    public override bool CompletesNormally =>
        EmittedCode.SequenceCompletesNormally(TrueBody) || EmittedCode.SequenceCompletesNormally(FalseBody);

    public override IEnumerable<IReadOnlyList<StatementItem>> NestedBodies => [TrueBody, FalseBody];
}