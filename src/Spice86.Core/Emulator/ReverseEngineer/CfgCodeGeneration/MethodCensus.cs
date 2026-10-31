namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

/// <summary>The per-method censuses of the printed code: its <c>goto</c>s, its status-flag reads, its
/// register webs, and the lifted IR block and statement counts a slow-method report prints.</summary>
internal sealed record MethodCensus(GotoCensus Gotos, FlagReadCensus FlagReads, WebCensus Webs,
    int LiftedBlocks, int LiftedStatements);
