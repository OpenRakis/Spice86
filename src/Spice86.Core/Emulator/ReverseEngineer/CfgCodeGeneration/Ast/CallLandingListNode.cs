namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Shared.Emulator.Memory;

/// <summary>The observed mid-call landings of one call site, printed as the arms of the wiring
/// constructor's <c>DefineCallLandings</c> line for the site's expected return address.</summary>
/// <param name="Arms">The observed landings to arm: each address and the generated entry running there.</param>
internal sealed record CallLandingListNode(
    IReadOnlyList<(SegmentedAddress Address, MethodGroupNode Entry)> Arms);
