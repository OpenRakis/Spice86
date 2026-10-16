namespace Spice86.Tests.AsmFixtures;

using Spice86.Shared.Emulator.Memory;

/// <summary>One instruction the interpreter was about to execute during a guarded run, with the evidence
/// needed to classify and report it: the CFG node kind, and the bytes the memory held at that address
/// when the interpreter was about to run it.</summary>
/// <param name="Address">Address of the node the interpreter was about to execute.</param>
/// <param name="NodeKind">Runtime type of that node, e.g. <c>CfgInstruction</c> or <c>SelectorNode</c>.</param>
/// <param name="InstructionBytes">Bytes of the instruction, as they were at that moment.</param>
internal sealed record ObservedExecution(SegmentedAddress Address, string NodeKind, byte[] InstructionBytes);
