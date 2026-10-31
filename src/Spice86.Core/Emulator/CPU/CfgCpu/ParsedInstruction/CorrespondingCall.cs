namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

/// <summary>
/// The call a return instruction corresponds to, and the successor type to record on the edge from
/// that call to the return's actual target.
/// </summary>
/// <param name="Call">The call instruction that probably caused the return.</param>
/// <param name="Type">The edge type to record from the call to the observed return target.</param>
public readonly record struct CorrespondingCall(CfgInstruction Call, InstructionSuccessorType Type);
