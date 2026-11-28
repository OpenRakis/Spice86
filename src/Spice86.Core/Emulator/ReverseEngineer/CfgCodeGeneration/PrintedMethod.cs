namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using System.Collections.Generic;

/// <summary>
/// One printed method and the printing facts its file builders consume: the segment that picks
/// its file, whether it was printed with a frame, whether it is emulator support code, its entry
/// address as a linear value for ordering, its name and its instruction line ranges for the
/// address map.
/// </summary>
internal sealed record PrintedMethod(ushort Segment, string Text, bool HasFrame, bool Support,
    uint EntryLinear, string MethodName, IReadOnlyList<MethodInstructionLine> Instructions);
