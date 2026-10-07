namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// The generated <c>throw FailAsUntested(...)</c> lines, one per untested situation; each message keeps the address
/// because the runtime log is where it is read.
/// </summary>
internal static class UntestedMessages {
    /// <summary>A conditional jump arm whose target was never observed.</summary>
    public static string Jump(SegmentedAddress at) => $"throw FailAsUntested(\"Untested jump at {at}\");";

    /// <summary>A conditional fallthrough arm that was never observed.</summary>
    public static string ConditionalFallthrough(SegmentedAddress at) => $"throw FailAsUntested(\"Untested fallthrough at {at}\");";

    /// <summary>A fallthrough after an instruction that was never observed.</summary>
    public static string FallthroughAfter(SegmentedAddress at) => $"throw FailAsUntested(\"Untested fallthrough after {at}\");";

    /// <summary>An indirect jump or call with no observed targets.</summary>
    public static string IndirectWithoutTargets(string targetKind, SegmentedAddress at) => $"throw FailAsUntested(\"Untested indirect {targetKind} at {at}\");";

    /// <summary>A near jump or call with an unknown runtime target.</summary>
    public static string NearTarget(string targetKind, string targetExpression, SegmentedAddress at) =>
        $"throw FailAsUntested($\"Untested near {targetKind} target 0x{{((ushort)({targetExpression})):X4}} at {at}\");";

    /// <summary>A far jump or call with an unknown runtime target.</summary>
    public static string FarTarget(string targetKind, string segmentVariable, string offsetVariable, SegmentedAddress at) =>
        $"throw FailAsUntested($\"Untested far {targetKind} target {{{segmentVariable}:X4}}:{{{offsetVariable}:X4}} at {at}\");";

    /// <summary>A self-modifying code selector with no matching signature.</summary>
    public static string CodeVariant(SegmentedAddress at) => $"throw FailAsUntested(\"Untested code variant at {at}\");";

    /// <summary>A call that returned but no continuation was observed during discovery.</summary>
    public static string ReturnFromCall(SegmentedAddress at) => $"throw FailAsUntested(\"Untested return from call at {at}\");";

    /// <summary>A CPU fault with an unknown target.</summary>
    public static string CpuFaultTarget(SegmentedAddress at) => $"throw FailAsUntested($\"Untested CPU fault target {{cpuFaultTarget}} at {at}\");";

    /// <summary>An entry dispatch with an unknown load offset.</summary>
    public static string EntryOffset() => "throw FailAsUntested($\"Untested entry offset 0x{loadOffset:X4}\");";
}
