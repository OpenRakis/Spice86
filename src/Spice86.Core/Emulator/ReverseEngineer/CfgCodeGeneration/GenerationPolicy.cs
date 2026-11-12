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
    UnknownMemoryIsRam = 1
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

    /// <summary>The policy name the differential report and the golden key record: the relaxation
    /// names joined, or <c>strict</c> when none is enabled.</summary>
    public string Name => Relaxations == GenerationRelaxation.None
        ? "strict"
        : string.Join(",", Enum.GetValues<GenerationRelaxation>()
            .Where(relaxation => relaxation != GenerationRelaxation.None
                && Relaxations.HasFlag(relaxation))
            .Select(relaxation => relaxation.ToString()));

    /// <summary>The golden-key suffix of the policy: <c>.ram</c> for
    /// <see cref="GenerationRelaxation.UnknownMemoryIsRam"/>, empty in strict mode.</summary>
    public string GoldenSuffix => AssumeUnknownMemoryIsRam ? ".ram" : "";

    /// <summary>The generated header line listing the enabled relaxations.</summary>
    public string HeaderLine => $"// Relaxations: {(Relaxations == GenerationRelaxation.None
        ? "none"
        : string.Join(", ", Enum.GetValues<GenerationRelaxation>()
            .Where(relaxation => relaxation != GenerationRelaxation.None
                && Relaxations.HasFlag(relaxation))
            .Select(relaxation => relaxation.ToString())))}";
}
