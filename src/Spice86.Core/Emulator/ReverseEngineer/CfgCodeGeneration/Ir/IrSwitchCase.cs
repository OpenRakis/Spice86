namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>One label of an <see cref="IrSwitch"/>.</summary>
/// <param name="Label">The case value.</param>
/// <param name="Target">The edge taken for the case value.</param>
internal sealed record IrSwitchCase(uint Label, IrEdge Target);
