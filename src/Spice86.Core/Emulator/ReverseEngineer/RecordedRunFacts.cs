namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Shared.Emulator.Memory;

/// <summary>Run facts the code generator needs that the CFG does not hold, recorded by DOS and the
/// mouse driver; a CFG reload merges the facts of earlier runs in.</summary>
public sealed class RecordedRunFacts {
    private readonly HashSet<SegmentedAddress> _terminateResumeTargets = [];

    /// <summary>The entries a terminate resumed at through the batch engine launching the next program.</summary>
    public IReadOnlySet<SegmentedAddress> TerminateResumeTargets => _terminateResumeTargets;

    /// <summary>Whether the program set the current PSP itself (AH=50h) or created a child PSP (AH=55h).</summary>
    public bool ProgramSetCurrentPsp { get; private set; }

    /// <summary>Whether a mouse user subroutine with a non-zero address was registered.</summary>
    public bool MouseUserSubroutineRegistered { get; private set; }

    /// <summary>Records an entry a terminate resumed at through a batch launch.</summary>
    public void RecordTerminateResume(SegmentedAddress target) => _terminateResumeTargets.Add(target);

    /// <summary>Records that the program set or created the current PSP itself.</summary>
    public void RecordProgramSetCurrentPsp() => ProgramSetCurrentPsp = true;

    /// <summary>Records that a mouse user subroutine with a non-zero address was registered.</summary>
    public void RecordMouseUserSubroutine() => MouseUserSubroutineRegistered = true;
}
