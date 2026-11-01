namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// One <c>private static readonly byte?[][]</c> field of the generated override class: the address of the
/// merged speculative run it verifies, the signature field names of its instructions in memory order, and
/// the field name.
/// </summary>
internal sealed record SpeculativeRunFieldPlan(SegmentedAddress Address, IReadOnlyList<string> SignatureFields, string FieldName);
