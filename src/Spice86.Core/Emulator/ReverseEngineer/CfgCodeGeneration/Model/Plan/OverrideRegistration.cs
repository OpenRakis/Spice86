namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

/// <summary>
/// A single <c>DefineFunction</c> registration the generated constructor emits for a partition entry.
/// <see cref="LoadOffset"/> is <c>0</c> for the primary entry (registered directly) and the entry offset
/// otherwise (registered through a <c>loadOffset</c>-passing lambda).
/// <para>
/// <see cref="BaseName"/> is the partition's address-free base name. Secondary-entry registrations name their
/// symbol from it (not from <see cref="MethodName"/>, which already carries the primary entry's address
/// triplet) so the dumped Ghidra symbol holds exactly one address triplet and round-trips through the symbol
/// file without accumulating duplicate triplets across generate cycles.
/// </para>
/// </summary>
internal sealed record OverrideRegistration(string SegmentVariable, ushort Offset, string MethodName, int LoadOffset, string BaseName);
