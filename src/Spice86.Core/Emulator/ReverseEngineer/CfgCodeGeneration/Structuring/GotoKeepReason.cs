namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

/// <summary>Why a <c>goto</c> survives structuring into the printed method: one reason per printed <c>goto</c>.</summary>
internal enum GotoKeepReason {
    /// <summary>A <c>goto</c> of the entry dispatch <c>switch (loadOffset)</c>.</summary>
    EntryDispatch,
    /// <summary>A <c>goto entrydispatcher;</c> of a cyclic cross-partition jump.</summary>
    CyclicJump,
    /// <summary>The target's block holds a partition entry.</summary>
    TargetIsEntry,
    /// <summary>The target's block is in a natural loop that is not structurable (a second entry).</summary>
    LoopWithSecondEntry,
    /// <summary>The target is the header of the innermost enclosing structured loop.</summary>
    LoopRestart,
    /// <summary>The target is outside the innermost enclosing structured loop.</summary>
    LoopExit,
    /// <summary>The target comes earlier in the layout than the source and does not dominate it.</summary>
    Irreducible,
    /// <summary>Refused branch: its condition reads a local variable.</summary>
    ConditionReadsLocals,
    /// <summary>Refused branch: the other arm is not a single <c>goto</c>.</summary>
    OtherArmNotGoto,
    /// <summary>Refused branch: this arm's head is outside the region being structured.</summary>
    ArmOutsideRegion,
    /// <summary>Refused branch: this arm's head is already placed.</summary>
    ArmHeadPlaced,
    /// <summary>Refused branch: this arm's head is a loop header.</summary>
    ArmHeadIsLoopHeader,
    /// <summary>Refused branch: this arm holds a latch of the enclosing loop.</summary>
    ArmHoldsLatch,
    /// <summary>Refused branch: this arm could be nested but does not come back to the other target, and the other arm cannot be nested.</summary>
    ArmNeverComesBack,
    /// <summary>Refused branch: both arms could be nested but they have no single common exit, or both come back.</summary>
    NoSingleJoin,
    /// <summary>A <c>switch</c> section that was not nested.</summary>
    SwitchSection,
    /// <summary>The target is the target of two or more printed <c>goto</c>s of the method.</summary>
    SharedTarget,
    /// <summary>None of the above: a single <c>goto</c> to a block that does not follow in the layout.</summary>
    NotNextInLayout
}
