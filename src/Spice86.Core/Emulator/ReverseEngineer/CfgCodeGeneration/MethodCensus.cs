namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

/// <summary>The per-method censuses of the printed code: its <c>goto</c>s, its register webs, and
/// the lifted IR block and statement counts a slow-method report prints. The status-flag read
/// census is computed later, on the promoted body each method prints with.</summary>
internal sealed record MethodCensus(GotoCensus Gotos, WebCensus Webs,
    int LiftedBlocks, int LiftedStatements);
