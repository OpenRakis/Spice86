namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

using System.Runtime.InteropServices;

/// <summary>
/// Records which registers each called routine was seen to give back unchanged. When an observed
/// near or far <c>call</c> executes, a frame with the registers before the call is pushed; when a
/// return lands on the frame's continuation with the same SS and the SP the call started from (a
/// balanced return), the registers still equal to their values at the call are ANDed into the
/// callee entry's mask. Only call and return instructions are looked at. Discovery-only
/// evidence: a wrong "preserved" answer can only propose a guarded speculative target. Not saved
/// in the dump; rebuilt as calls execute.
/// </summary>
internal sealed class ObservedCallPreservation {
    internal const int MaxFrames = 256;
    private const int FrameIndexMask = MaxFrames - 1;
    private const int GeneralRegisterCount = 8;
    private const int SegmentRegisterCount = 6;
    private const int RegisterCount = GeneralRegisterCount + SegmentRegisterCount;
    private const int SegmentBitShift = 8;

    /// <summary>One call in flight: the callee entry it jumped to, the address after the call,
    /// and SS:SP right before the call executed.</summary>
    private struct Frame {
        public SegmentedAddress Entry;
        public SegmentedAddress Continuation;
        public ushort Ss;
        public ushort Sp;
    }

    /// <summary>The registers a callee left unchanged on every recorded return, and how many
    /// returns were recorded.</summary>
    private readonly record struct Evidence(ushort Preserved, int Count);

    private readonly State _state;
    private readonly Frame[] _frames = new Frame[MaxFrames];
    private readonly uint[] _registers = new uint[MaxFrames * RegisterCount];
    private readonly Dictionary<SegmentedAddress, Evidence> _byCallee = new();
    private int _bottom;
    private int _count;
    private bool _awaitingEntry;

    public ObservedCallPreservation(State state) {
        _state = state;
    }

    /// <summary>Changes whenever a recorded mask shrinks or an entry is forgotten, so a cached
    /// answer computed from earlier evidence can tell it is stale. A first record does not change
    /// it: no cached answer can depend on evidence that did not exist.</summary>
    public int Version { get; private set; }

    /// <summary>Called before a call or return instruction executes: a near or far call opens a
    /// frame with the registers it starts from.</summary>
    public void BeforeTransfer(CfgInstruction transfer) {
        if (transfer.IsCall && IsNearOrFarCall(transfer)) {
            OpenFrame(transfer);
        }
    }

    /// <summary>Called after a call or return instruction executed: a call records the target it
    /// took as the frame's callee entry (a faulted call drops its frame); a return that lands on
    /// the top frame's continuation balanced records the registers it kept.</summary>
    public void AfterTransfer(CfgInstruction transfer, bool faulted) {
        if (_awaitingEntry) {
            _awaitingEntry = false;
            if (faulted) {
                PopTop();
                return;
            }
            _frames[TopIndex].Entry = CurrentCsIp;
            return;
        }
        if (transfer.IsReturn && !faulted && _count != 0) {
            OnReturned();
        }
    }

    /// <summary>The registers the callee at <paramref name="calleeEntry"/> gave back unchanged
    /// on every recorded balanced return (SP always included), and the number of such
    /// returns. False when none was recorded.</summary>
    public bool TryGetPreserved(SegmentedAddress calleeEntry, out ushort preserved, out int count) {
        if (_byCallee.TryGetValue(calleeEntry, out Evidence evidence)) {
            preserved = evidence.Preserved;
            count = evidence.Count;
            return true;
        }
        preserved = 0;
        count = 0;
        return false;
    }

    /// <summary>Forgets the evidence of the callee entry at <paramref name="calleeEntry"/>: its
    /// entry node was removed or replaced.</summary>
    public void Forget(SegmentedAddress calleeEntry) {
        if (_byCallee.Remove(calleeEntry)) {
            Version++;
        }
    }

    private static bool IsNearOrFarCall(CfgInstruction call) {
        IVisitableAstNode? ast = call.ExecutionAst;
        IVisitableAstNode? last = ast is BlockNode block && block.Statements.Count > 0
            ? block.Statements[^1]
            : ast;
        return last is CallNearNode or CallFarNode;
    }

    private ushort CurrentSs => (ushort)_state.SegmentRegisters.Values[(int)SegmentRegisterIndex.SsIndex];

    private ushort CurrentSp => (ushort)_state.GeneralRegisters.Values[(int)RegisterIndex.SpIndex];

    private SegmentedAddress CurrentCsIp =>
        new((ushort)_state.SegmentRegisters.Values[(int)SegmentRegisterIndex.CsIndex], _state.IP);

    private int TopIndex => (_bottom + _count - 1) & FrameIndexMask;

    private void OpenFrame(CfgInstruction call) {
        DropStaleFrames();
        if (_count == MaxFrames) {
            _bottom = (_bottom + 1) & FrameIndexMask;
            _count--;
        }
        _count++;
        int index = TopIndex;
        _frames[index] = new Frame {
            Continuation = call.NextInMemoryAddress32.ToSegmentedAddress(),
            Ss = CurrentSs,
            Sp = CurrentSp
        };
        Span<uint> saved = _registers.AsSpan(index * RegisterCount, RegisterCount);
        _state.GeneralRegisters.Values.CopyTo(saved);
        _state.SegmentRegisters.Values.CopyTo(saved[GeneralRegisterCount..]);
        _awaitingEntry = true;
    }

    /// <summary>Drops the frames whose call already returned or was unwound: a frame on the
    /// current stack whose SP before the call is at or below the current SP (wrap-aware), since
    /// inside a callee SP stays below the SP its call started from.</summary>
    private void DropStaleFrames() {
        while (_count != 0) {
            Frame top = _frames[TopIndex];
            if (top.Ss != CurrentSs || IsInsideCallee(top.Sp, CurrentSp)) {
                return;
            }
            PopTop();
        }
    }

    private static bool IsInsideCallee(ushort spBeforeCall, ushort currentSp) {
        ushort depth = (ushort)(spBeforeCall - currentSp);
        return depth != 0 && depth < 0x8000;
    }

    private void OnReturned() {
        int index = TopIndex;
        Frame top = _frames[index];
        if (top.Continuation != CurrentCsIp || top.Ss != CurrentSs || top.Sp != CurrentSp) {
            return;
        }
        ushort preserved = UnchangedRegisters(_registers.AsSpan(index * RegisterCount, RegisterCount));
        ref Evidence evidence = ref CollectionsMarshal.GetValueRefOrAddDefault(_byCallee, top.Entry, out bool exists);
        if (!exists) {
            evidence = new Evidence(preserved, 1);
        } else {
            ushort kept = (ushort)(evidence.Preserved & preserved);
            if (kept != evidence.Preserved) {
                Version++;
            }
            evidence = new Evidence(kept, evidence.Count + 1);
        }
        PopTop();
    }

    private ushort UnchangedRegisters(ReadOnlySpan<uint> saved) {
        ReadOnlySpan<uint> general = _state.GeneralRegisters.Values;
        ReadOnlySpan<uint> segments = _state.SegmentRegisters.Values;
        ushort preserved = 0;
        for (int register = 0; register < GeneralRegisterCount; register++) {
            if (saved[register] == general[register]) {
                preserved |= (ushort)(1 << register);
            }
        }
        for (int segment = 0; segment < SegmentRegisterCount; segment++) {
            if (saved[GeneralRegisterCount + segment] == segments[segment]) {
                preserved |= (ushort)(1 << (SegmentBitShift + segment));
            }
        }
        return preserved;
    }

    private void PopTop() => _count--;
}
