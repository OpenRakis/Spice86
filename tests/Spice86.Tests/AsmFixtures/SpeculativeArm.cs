namespace Spice86.Tests.AsmFixtures;

/// <summary>The arm of a speculative fixture to run.</summary>
internal enum SpeculativeArm {
    /// <summary>The arm the discovery run executes, selector byte 0000:0500 = 0.</summary>
    Observed,
    /// <summary>The other arm, selector byte = 1. Only speculative discovery explores it.</summary>
    Unobserved
}
