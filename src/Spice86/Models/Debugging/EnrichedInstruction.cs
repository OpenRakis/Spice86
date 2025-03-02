namespace Spice86.Models.Debugging;

using Iced.Intel;

using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.Memory;
using Spice86.ViewModels;

/// <summary>
/// An Iced assembly instruction, enriched with additional information.
/// </summary>
/// <param name="instruction">The Iced assembly instruction</param>
public class EnrichedInstruction(Instruction instruction) {
    public Instruction Instruction { get; } = instruction;
    public byte[] Bytes { get; init; } = [];
    public FunctionInformation? Function { get; init; }
    public SegmentedAddress SegmentedAddress { get; init; }
    public List<BreakpointViewModel> Breakpoints { get; init; } = [];
}