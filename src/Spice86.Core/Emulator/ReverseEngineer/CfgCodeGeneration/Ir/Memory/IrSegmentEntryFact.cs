namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>The entry summary of one entry block: <see cref="KnownValues"/> holds the registers
/// whose value is the same constant at every way in, <see cref="EqualTo"/> maps each register of an
/// equality class to the register whose entry version roots the class (the first member in DS, ES,
/// FS, GS order). A reached register appearing in neither is unknown.</summary>
internal sealed record IrSegmentEntryFact(
    IReadOnlyDictionary<IrSsaValue, ushort> KnownValues,
    IReadOnlyDictionary<IrSsaValue, IrSsaValue> EqualTo);
