namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

using System.Linq;

/// <summary>
/// Watches pointer-shaped memory cells (a near pointer is one word, a far pointer two) for the
/// writes that change them, and re-evaluates the cell's value at the next instruction boundary.
/// A single <see cref="MultiAddressBreakPoint"/> armed on every watched byte keeps a write outside
/// the cells at one bit test. Write breakpoints fire before the byte is stored, so the hit only
/// marks the touched word and queues the cell: evaluation runs on the fetch/link path, when memory
/// already holds the new value, at the cost of one emptiness test when nothing is queued.
/// A cell re-evaluates only once every word it is made of was written since its last evaluation,
/// so a half-written far pointer is never read; a word counts as written on any write that touches
/// it, even a store of the same value (a re-install in the same segment rewrites the segment word
/// unchanged and must still count). A value identical to the previous one is not reported.
/// </summary>
public sealed class WatchedPointerCells {
    private readonly IMemory _memory;
    private readonly AddressReadWriteBreakpoints _writeBreakpoints;
    private readonly MultiAddressBreakPoint _armed;
    private readonly List<WatchedPointerCell> _cells = new();
    private readonly Queue<WatchedPointerCell> _queued = new();
    private readonly HashSet<WatchedPointerCell> _inQueue = new();

    /// <summary>
    /// One consumer of a re-evaluated cell value. Dependents are small records rather than a
    /// vector-specific field so later watchers (for instance a pointer read by a speculative site)
    /// can attach their own re-evaluation to the same cell machinery.
    /// </summary>
    /// <param name="OnValue">Called with the cell's new value and the instruction that executed at
    /// the boundary where the write completed (the writing instruction).</param>
    internal sealed record WatchedCellDependent(Action<uint, CfgInstruction?> OnValue);

    internal sealed class WatchedPointerCell {
        internal WatchedPointerCell(uint start, int size, WatchedCellDependent dependent) {
            Start = start;
            Size = size;
            Dependent = dependent;
        }

        internal uint Start { get; }
        internal int Size { get; }
        internal WatchedCellDependent Dependent { get; }
        internal int WrittenWords { get; set; }
        internal uint? LastValue { get; set; }
        private int WordCount => Size / 2;
        internal bool FullyWritten => WrittenWords == (1 << WordCount) - 1;
        internal bool PartiallyWritten => WrittenWords != 0 && !FullyWritten;
    }

    /// <summary>
    /// Creates the set and its armed breakpoint; nothing is watched until <see cref="Watch"/> runs.
    /// </summary>
    /// <param name="memory">The memory bus, read to evaluate cell values after their writes.</param>
    /// <param name="writeBreakpoints">The write breakpoint holder the armed breakpoint installs into.</param>
    public WatchedPointerCells(IMemory memory, AddressReadWriteBreakpoints writeBreakpoints) {
        _memory = memory;
        _writeBreakpoints = writeBreakpoints;
        _armed = new MultiAddressBreakPoint(BreakPointType.MEMORY_WRITE, OnWatchedWrite);
    }

    /// <summary>
    /// Starts watching the <paramref name="size"/> bytes at <paramref name="physicalStart"/> as one
    /// pointer cell. The value baseline is set by the first evaluation, so watching costs nothing
    /// (and works on memory sizes that do not cover the cell yet).
    /// </summary>
    internal void Watch(uint physicalStart, int size, WatchedCellDependent dependent) {
        _cells.Add(new WatchedPointerCell(physicalStart, size, dependent));
        for (uint address = physicalStart; address < physicalStart + size; address++) {
            _writeBreakpoints.ToggleBreakPointAt(_armed, AddressOperation.WRITE, address, true);
        }
    }

    /// <summary>The far cells that saw one word written and are still waiting for the other; the
    /// value they would produce is never evaluated until the second write arrives.</summary>
    internal IReadOnlyList<WatchedPointerCell> PartiallyWrittenCells =>
        _cells.Where(cell => cell.PartiallyWritten).ToList();

    /// <summary>
    /// Dump-time completion: evaluates every cell still waiting for a second write (a far pointer
    /// whose other word never came) on the value memory holds now, with no trigger instruction.
    /// The read pairs the newly written word with the old value of the missing one; dependents
    /// apply the same trust checks as boundary evaluations, so a pointer that mixes a program
    /// write with an unrelated previous owner decodes only when its target bytes are trusted.
    /// </summary>
    internal void EvaluatePartiallyWritten() {
        foreach (WatchedPointerCell cell in PartiallyWrittenCells) {
            cell.WrittenWords = 0;
            uint value = ReadValue(cell.Start, cell.Size);
            if (value == cell.LastValue) {
                continue;
            }
            cell.LastValue = value;
            cell.Dependent.OnValue(value, null);
        }
    }

    /// <summary>
    /// Re-evaluates every queued cell whose words were all written since its last evaluation, then
    /// reports the ones whose value changed. Called once per instruction boundary; costs one
    /// emptiness test when nothing is queued.
    /// </summary>
    /// <param name="trigger">The instruction that executed just before this boundary.</param>
    internal void EvaluateQueued(CfgInstruction? trigger) {
        while (_queued.Count != 0) {
            WatchedPointerCell cell = _queued.Dequeue();
            _inQueue.Remove(cell);
            if (!cell.FullyWritten) {
                // Only part of the pointer was written: keep the flags, the next write requeues it.
                continue;
            }
            cell.WrittenWords = 0;
            uint value = ReadValue(cell.Start, cell.Size);
            if (value == cell.LastValue) {
                continue;
            }
            cell.LastValue = value;
            cell.Dependent.OnValue(value, trigger);
        }
    }

    private void OnWatchedWrite(BreakPoint breakPoint, long address) {
        uint touched = (uint)address;
        foreach (WatchedPointerCell cell in _cells) {
            uint offset = touched - cell.Start;
            if (offset >= (uint)cell.Size) {
                continue;
            }
            cell.WrittenWords |= 1 << (int)(offset >> 1);
            if (_inQueue.Add(cell)) {
                _queued.Enqueue(cell);
            }
        }
    }

    private uint ReadValue(uint start, int size) {
        uint value = 0;
        for (int i = 0; i < size; i++) {
            value |= (uint)_memory.SneakilyRead(start + (uint)i) << (8 * i);
        }
        return value;
    }
}
