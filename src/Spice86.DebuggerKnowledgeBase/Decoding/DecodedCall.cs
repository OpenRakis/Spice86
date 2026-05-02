namespace Spice86.DebuggerKnowledgeBase.Decoding;

using System.Collections.Generic;

/// <summary>
/// Describes a high-level call decoded from the emulator state — an interrupt invocation,
/// an I/O port access, or a hit on an emulator-installed ASM routine entry point.
/// </summary>
/// <param name="Subsystem">Subsystem the call targets (e.g. "DOS INT 21h", "Sound Blaster", "OPL3", "PIC1").</param>
/// <param name="FunctionName">Function/operation name within that subsystem (e.g. "AH=3Dh Open File").</param>
/// <param name="ShortDescription">One-line description suitable for a tooltip header.</param>
/// <param name="Parameters">Inputs of the call. Empty when none could be decoded.</param>
/// <param name="Results">Outputs of the call (typically only present after the call returned).</param>
public sealed record DecodedCall(
    string Subsystem,
    string FunctionName,
    string ShortDescription,
    IReadOnlyList<DecodedParameter> Parameters,
    IReadOnlyList<DecodedParameter> Results);
