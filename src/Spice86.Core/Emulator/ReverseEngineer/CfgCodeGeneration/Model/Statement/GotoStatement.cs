namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>A same-method jump to the block entry <paramref name="Target"/>. The label text is resolved at render time.</summary>
internal sealed record GotoStatement(ICfgNode Target) : StatementItem {
    public override bool CompletesNormally => false;
    public override IEnumerable<IReadOnlyList<StatementItem>> NestedBodies => [];
}
