namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

/// <summary>
/// One push contribution to a stack slot: the statement that wrote the bytes and the delta below
/// the current base where they landed.
/// </summary>
internal readonly record struct IrStackSlotDefinition(IrStatement Statement, int Delta);
