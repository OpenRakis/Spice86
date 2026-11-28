namespace Spice86.Shared.Emulator.Errors;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Thrown by a generated DOS terminate service (<c>Dos.Terminate</c>,
/// <c>Dos.TerminateAndStayResident</c>) after process termination restored the parent context:
/// carries the resume address as a <see cref="CallStackUnwindException"/> target so the pending
/// call that accepts it continues there, exactly like an unwound <c>int</c> stub landing on the
/// terminate address. Also names the terminated program's PSP for the parent/child EXEC handling.
/// </summary>
public class ProcessTerminatedException : CallStackUnwindException {
    /// <summary>
    /// Initializes a new instance of the ProcessTerminatedException class.
    /// </summary>
    /// <param name="target">The address process termination left in CS:IP (the parent's resume
    /// address, usually the child's INT 22h terminate vector target).</param>
    /// <param name="terminatedPsp">The PSP segment of the process that terminated.</param>
    public ProcessTerminatedException(SegmentedAddress target, ushort terminatedPsp)
        : base(target,
            $"DOS process at PSP {terminatedPsp:X4} terminated; control resumes at {target}.") {
        TerminatedPsp = terminatedPsp;
    }

    /// <summary>
    /// The PSP segment of the process that terminated.
    /// </summary>
    public ushort TerminatedPsp { get; }
}
