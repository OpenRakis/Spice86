namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// The DOS child-process bodies of <see cref="CSharpOverrideHelper"/>: EXEC-and-run calls,
/// internal-program children and batch-program resumes.
/// </summary>
internal sealed class DosChildProcessRunner {
    private readonly State _state;
    private readonly Stack _stack;
    private readonly IMemory _memory;
    private readonly Func<ushort> _readPspAddress;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly Func<string, UnrecoverableException> _failAsUntested;
    private readonly Func<SegmentedAddress, Func<int, Action>?> _searchFunctionOverride;

    /// <summary>Creates the runner over the machine's CPU state, stack, memory and interrupt
    /// vector table.</summary>
    /// <param name="readPspAddress">Reads the current PSP address through the installed DOS
    /// handler; a delegate because the handler resolves lazily.</param>
    public DosChildProcessRunner(State state, Stack stack, IMemory memory,
        Func<ushort> readPspAddress, InterruptVectorTable interruptVectorTable,
        Func<string, UnrecoverableException> failAsUntested,
        Func<SegmentedAddress, Func<int, Action>?> searchFunctionOverride) {
        _state = state;
        _stack = stack;
        _memory = memory;
        _readPspAddress = readPspAddress;
        _interruptVectorTable = interruptVectorTable;
        _failAsUntested = failAsUntested;
        _searchFunctionOverride = searchFunctionOverride;
    }

    /// <summary>Runs the managed internal-program child to its terminate, then resumes the
    /// parent's observed fallthrough at <paramref name="resumeCs"/>:<paramref name="resumeIp"/>;
    /// a terminate targeting anywhere else propagates and a normal return fails as untested.</summary>
    public Action RunInternalProgramChild(ushort resumeCs, ushort resumeIp,
        Func<int, Action> child, Func<int, Action> continuation) => () => {
        try {
            child(0).Invoke();
        } catch (ProcessTerminatedException exception)
            when (exception.Target == new SegmentedAddress(resumeCs, resumeIp)) {
            continuation(0).Invoke();
            return;
        }
        throw _failAsUntested(
            $"The internal program child returned normally instead of terminating or resuming at {resumeCs:X4}:{resumeIp:X4}");
    };

    /// <summary>The root entry of a program a terminate can resume through the batch engine:
    /// while a terminate resumes at an entry <paramref name="batchPrograms"/> arms, runs that
    /// entry; a resume at an address discovery never armed fails as untested.</summary>
    public Func<int, Action> RunBatchPrograms(Func<int, Action> program,
        params CallLanding[] batchPrograms) => loadOffset => () => {
        Func<int, Action> current = program;
        int currentLoadOffset = loadOffset;
        while (true) {
            try {
                current.Invoke(currentLoadOffset).Invoke();
                return;
            } catch (ProcessTerminatedException exception) {
                Func<int, Action>? resume = null;
                foreach (CallLanding landing in batchPrograms) {
                    if (landing.Address == exception.Target) {
                        resume = landing.Entry;
                        break;
                    }
                }
                if (resume is null) {
                    throw _failAsUntested(
                        $"The root program's terminate resumed at {exception.Target}, which discovery did not observe as a batch launch");
                }
                current = resume;
                currentLoadOffset = 0;
            }
        }
    };

    /// <summary>The typed DOS EXEC-and-run call: pushes the <c>int</c> frame the interpreted
    /// call pushed, clears IF like interrupt entry, then invokes the generated interrupt stub of
    /// the call's vector; the child's terminate unwinds here through the pushed frame.</summary>
    public (ushort ax, bool carryFlag, ushort ds, ushort es) ExecChild(
        ushort expectedReturnCs, ushort expectedReturnIp, byte vectorNumber) {
        ushort parentPsp = _readPspAddress();
        SegmentedAddress isrEntry = _interruptVectorTable[vectorNumber];
        Func<int, Action>? stub = _searchFunctionOverride(isrEntry);
        if (stub is null) {
            throw _failAsUntested(
                $"The EXEC call's interrupt 0x{vectorNumber:X2} stub {isrEntry} was not generated");
        }
        _stack.PushInterruptFrame(new SegmentedAddress(expectedReturnCs, expectedReturnIp));
        _state.InterruptFlag = false;
        try {
            stub.Invoke(0).Invoke();
        } catch (ProcessTerminatedException exception) when (IsChildTerminate(exception, parentPsp)) {
            // The child's own terminate restored this parent: resume exactly like the
            // interpreted iret landing on the frame this call pushed.
        } catch (CallStackUnwindException exception)
            when (exception.Target == new SegmentedAddress(expectedReturnCs, expectedReturnIp)) {
            // A resume landing a call chain inside the child could not route: the child's
            // terminate already restored this parent, so the unwind is this call's return.
        }
        SegmentedAddress resumed = _state.IpSegmentedAddress;
        if (resumed == new SegmentedAddress(expectedReturnCs, expectedReturnIp)) {
            return (_state.AX, _state.CarryFlag, _state.DS, _state.ES);
        }
        if (_searchFunctionOverride(resumed) is not null) {
            throw _failAsUntested(
                $"The EXEC call expecting to return to {expectedReturnCs:X4}:{expectedReturnIp:X4} landed at {resumed}, which discovery did not observe it landing on");
        }
        throw new CallStackUnwindException(resumed,
            $"DOS EXEC child terminated; control resumes at {resumed}.");
    }

    /// <summary>Whether a process termination is this EXEC's own child: the terminated
    /// process's parent PSP is the PSP that was current when the call entered.</summary>
    private bool IsChildTerminate(ProcessTerminatedException exception, ushort parentPsp) {
        DosProgramSegmentPrefix psp = new(_memory,
            MemoryUtils.ToPhysicalAddress(exception.TerminatedPsp, 0));
        return psp.ParentProgramSegmentPrefix == parentPsp;
    }
}
