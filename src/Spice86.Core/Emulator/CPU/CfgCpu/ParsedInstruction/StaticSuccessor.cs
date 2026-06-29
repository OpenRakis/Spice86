namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A statically-known successor address paired with its edge type.
/// Populated by parsers at decode time to express the control-flow role of each successor:
/// <list type="bullet">
///   <item><description><see cref="InstructionSuccessorType.Normal"/>: fall-through, branch target,
///     or callee entry (for CALL near imm).</description></item>
///   <item><description><see cref="InstructionSuccessorType.CallToReturn"/>: the continuation
///     address after a CALL/INT, computed from instruction length. Only registered by parsers
///     when the continuation is statically knowable and meaningful (currently unused by parsers;
///     reserved for future use by the known-safe handler seeding explorer).</description></item>
/// </list>
/// </summary>
/// <param name="Address">Target address of the successor.</param>
/// <param name="EdgeType">Control-flow edge classification.</param>
public readonly record struct StaticSuccessor(SegmentedAddress Address, InstructionSuccessorType EdgeType);
