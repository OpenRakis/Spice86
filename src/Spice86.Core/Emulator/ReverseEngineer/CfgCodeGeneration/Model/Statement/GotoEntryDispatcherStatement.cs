namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

/// <summary>A jump back to the method's entry dispatcher (cyclic cross-partition re-entry).</summary>
internal sealed record GotoEntryDispatcherStatement : StatementItem {
    public override bool CompletesNormally => false;
    public override IEnumerable<IReadOnlyList<StatementItem>> NestedBodies => [];
}
