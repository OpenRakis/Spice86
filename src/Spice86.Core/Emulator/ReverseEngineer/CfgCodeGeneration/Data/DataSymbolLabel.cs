namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A data symbol with a known segmented address, exported to the Ghidra symbols file as a label.
/// </summary>
internal sealed record DataSymbolLabel(string Name, SegmentedAddress Address);
