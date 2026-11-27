namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

/// <summary>Test-side members of <see cref="GenerationPolicy"/>: only the tests name goldens.</summary>
internal static class GenerationPolicyExtensions {
    /// <summary>The golden-key suffix part of each relaxation, in flag order.</summary>
    private static readonly (GenerationRelaxation Flag, string Suffix)[] GoldenSuffixParts = [
        (GenerationRelaxation.UnknownMemoryIsRam, ".ram"),
        (GenerationRelaxation.AccessesStayInSegment, ".seg"),
        (GenerationRelaxation.AbandonedStackSlotsUnobserved, ".stk"),
        (GenerationRelaxation.SavedSlotsWrittenOnlyByPops, ".sav")];

    /// <summary>The golden-key suffix of the policy: each enabled relaxation's suffix part
    /// concatenated in flag order, empty in strict mode.</summary>
    public static string GoldenSuffix(this GenerationPolicy policy) => string.Concat(GoldenSuffixParts
        .Where(part => policy.Relaxations.HasFlag(part.Flag))
        .Select(part => part.Suffix));
}
