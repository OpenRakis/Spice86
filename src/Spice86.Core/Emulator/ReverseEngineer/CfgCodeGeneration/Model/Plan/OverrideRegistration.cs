namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

/// <summary>
/// A single <c>DefineFunction</c> registration the generated constructor emits for a partition entry.
/// <see cref="LoadOffset"/> is <c>0</c> for the primary entry (registered directly) and the entry offset
/// otherwise (registered through a <c>loadOffset</c>-passing lambda).
/// <para>
/// <see cref="BaseName"/> is the partition's address-free base name. Every registration names its symbol
/// from it (primary entries directly, secondary entries with an <c>_entry</c> tag), not from
/// <see cref="MethodName"/>, which carries the primary entry's address triplet and, for colliding self-modifying
/// variants, a node id. The dumped Ghidra symbol therefore holds exactly one address triplet and round-trips
/// through the symbol file without accumulating duplicate triplets or node ids across generate cycles.
/// </para>
/// </summary>
internal sealed record OverrideRegistration(string SegmentVariable, ushort Offset, string MethodName, int LoadOffset, string BaseName);
