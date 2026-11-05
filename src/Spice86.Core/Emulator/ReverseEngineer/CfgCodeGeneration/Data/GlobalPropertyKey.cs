namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;

using Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// What a generated global property covers: an access through a segment register
/// (<see cref="RegisterGlobalPropertyKey"/>) or a proven location
/// (<see cref="LocationGlobalPropertyKey"/>).
/// </summary>
internal abstract record GlobalPropertyKey(ushort Offset, int Width);
