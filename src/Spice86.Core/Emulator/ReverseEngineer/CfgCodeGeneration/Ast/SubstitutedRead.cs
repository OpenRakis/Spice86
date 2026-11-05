namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>How one node instance prints as a register local: the exact <paramref name="Text"/> and
/// the fragment's <paramref name="Type"/>. <paramref name="Text"/> is the bare local name, or the
/// name cast back to the node's type when the local's type differs from the read node's; it is the
/// bare name with <paramref name="Type"/> set to the local's type only where the direct parent is a
/// conversion to that type (the parent then drops the conversion) or where the node is a definition
/// target (its right side is cast to the local's type). A <see langword="null"/>
/// <paramref name="Type"/> means the node's own type.</summary>
internal sealed record SubstitutedRead(string Text, DataType? Type);
