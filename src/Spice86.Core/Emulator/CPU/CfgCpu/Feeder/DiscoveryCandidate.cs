namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// One discovery target submitted to <see cref="SpeculativeExplorer.ExploreCandidates"/> by a
/// trigger (an instruction whose execution produced the value). <paramref name="Predecessor"/> is
/// the instruction the edge is wired from, or null for a root entry. Data-sourced candidates are
/// decoded only while their target bytes still hold the values their loader wrote; until then they
/// are kept pending on the trigger and retried each time it fires.
/// </summary>
public readonly record struct DiscoveryCandidate(
    SegmentedAddress Target,
    CfgInstruction? Predecessor,
    InstructionSuccessorType EdgeType,
    ExploreSource Source);
