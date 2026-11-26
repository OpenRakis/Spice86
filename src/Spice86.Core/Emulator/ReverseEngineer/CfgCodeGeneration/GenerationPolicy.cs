namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using System.Linq;

/// <summary>The named relaxations a generation run may enable, each one command-line option. An
/// assumption fills missing knowledge and never overrides a contrary fact.</summary>
[Flags]
internal enum GenerationRelaxation {
    /// <summary>No relaxation: strict mode, the baseline.</summary>
    None = 0,
    /// <summary>Memory the alias analysis cannot prove is treated as ordinary RAM.</summary>
    UnknownMemoryIsRam = 1,
    /// <summary>An indexed access whose offset range is unproved is assumed not to wrap the
    /// segment end; a constant or proved range that crosses stays a fact.</summary>
    AccessesStayInSegment = 2,
    /// <summary>Bytes a private typed stack argument occupied are not observed after the call
    /// returns: the site reserves the same stack space without writing the argument bytes. The
    /// same assumption covers the bytes a removed save push leaves on the stack after its pop: a
    /// save pair is removed only when no proved read of those bytes exists, and the differential
    /// gate excludes them through <c>DroppedStackSlotSites</c>.</summary>
    AbandonedStackSlotsUnobserved = 4,
    /// <summary>A slot a save push of a register wrote keeps its bytes across a call or service
    /// boundary when the method lets no stack address escape; the pop still restores them.</summary>
    SavedSlotsWrittenOnlyByPops = 8
}

/// <summary>The immutable set of relaxations the whole generation run shares: effects, SSA,
/// summaries, passes and emission read this one object. The default is <see cref="Strict"/>.</summary>
/// <param name="Relaxations">The enabled relaxations.</param>
internal sealed record GenerationPolicy(GenerationRelaxation Relaxations) {
    /// <summary>Strict mode: every fact must be proved.</summary>
    public static readonly GenerationPolicy Strict = new(GenerationRelaxation.None);

    /// <summary>Whether unknown memory is treated as ordinary RAM.</summary>
    public bool AssumeUnknownMemoryIsRam =>
        Relaxations.HasFlag(GenerationRelaxation.UnknownMemoryIsRam);

    /// <summary>Whether an indexed access with an unproved offset range is assumed not to wrap
    /// the segment end.</summary>
    public bool AssumeAccessesStayInSegment =>
        Relaxations.HasFlag(GenerationRelaxation.AccessesStayInSegment);

    /// <summary>Whether private typed stack argument bytes are assumed unobserved after the
    /// call returns, letting the call site reserve their stack space without writing them. Also
    /// covers the bytes a removed save push leaves on the stack after its pop, which the
    /// differential gate excludes through <c>DroppedStackSlotSites</c>.</summary>
    public bool AssumeAbandonedStackSlotsUnobserved =>
        Relaxations.HasFlag(GenerationRelaxation.AbandonedStackSlotsUnobserved);

    /// <summary>Whether saved-register slots are assumed written only by their pops, letting them
    /// survive call and service boundaries in a method with no escaping stack address.</summary>
    public bool AssumeSavedSlotsWrittenOnlyByPops =>
        Relaxations.HasFlag(GenerationRelaxation.SavedSlotsWrittenOnlyByPops);

    /// <summary>The policy name the differential report and the golden key record: the relaxation
    /// names joined, or <c>strict</c> when none is enabled.</summary>
    public string Name => Relaxations == GenerationRelaxation.None
        ? "strict"
        : string.Join(",", Enum.GetValues<GenerationRelaxation>()
            .Where(relaxation => relaxation != GenerationRelaxation.None
                && Relaxations.HasFlag(relaxation))
            .Select(relaxation => relaxation.ToString()));

    /// <summary>The golden-key suffix part of each relaxation, in flag order.</summary>
    private static readonly (GenerationRelaxation Flag, string Suffix)[] GoldenSuffixParts = [
        (GenerationRelaxation.UnknownMemoryIsRam, ".ram"),
        (GenerationRelaxation.AccessesStayInSegment, ".seg"),
        (GenerationRelaxation.AbandonedStackSlotsUnobserved, ".stk"),
        (GenerationRelaxation.SavedSlotsWrittenOnlyByPops, ".sav")];

    /// <summary>The golden-key suffix of the policy: each enabled relaxation's suffix part
    /// concatenated in flag order, empty in strict mode.</summary>
    public string GoldenSuffix => string.Concat(GoldenSuffixParts
        .Where(part => Relaxations.HasFlag(part.Flag))
        .Select(part => part.Suffix));

    /// <summary>The generated header line listing the enabled relaxations.</summary>
    public string HeaderLine => $"// Relaxations: {(Relaxations == GenerationRelaxation.None
        ? "none"
        : string.Join(", ", Enum.GetValues<GenerationRelaxation>()
            .Where(relaxation => relaxation != GenerationRelaxation.None
                && Relaxations.HasFlag(relaxation))
            .Select(relaxation => relaxation.ToString())))}";
}
