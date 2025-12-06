namespace Spice86.ViewModels.ValueViewModels.Debugging;

using Iced.Intel;

using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.Memory;
using Spice86.ViewModels;
using Spice86.ViewModels.TextPresentation;

using System.Collections.Immutable;

/// <summary>
/// An Iced assembly instruction, enriched with additional information.
/// </summary>
/// <param name="Instruction">The Iced assembly instruction</param>
public record EnrichedInstruction(Instruction Instruction) {
    public byte[] Bytes { get; init; } = [];
    public FunctionInformation? Function { get; init; }
    public SegmentedAddress SegmentedAddress { get; init; }
    public ImmutableList<BreakpointViewModel> Breakpoints { get; init; } = [];

    /// <summary>
    /// Gets or sets a custom formatted representation of the instruction.
    /// If null, the default formatting from Iced will be used.
    /// </summary>
    public List<FormattedTextSegment>? InstructionFormatOverride { get; init; }
}