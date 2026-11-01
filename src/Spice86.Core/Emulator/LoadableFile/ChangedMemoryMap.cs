namespace Spice86.Core.Emulator.LoadableFile;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

using System.Collections;
using System.Linq;

/// <summary>
/// Records which bytes of the loaded program images changed after their loader finished. A single
/// <see cref="MultiAddressBreakPoint"/> is registered at every watched byte that has not changed
/// yet, so a write outside the images costs one bit test and a write to an already-changed byte
/// meets no breakpoint and is never reported again. A byte is "changed" the first time a write
/// stores a value different from what it holds. Always on: the map subscribes to
/// <see cref="LoadedImageRegistry.ImageLoaded"/>, so a loader's own writes are never counted, while a
/// later load into an already watched area marks the bytes it changes.
/// </summary>
public sealed class ChangedMemoryMap {
    private const int CoveredSize = (int)A20Gate.EndOfHighMemoryArea + 1;

    private readonly Memory _memory;
    private readonly AddressReadWriteBreakpoints _writeBreakpoints;
    private readonly BitArray _watched = new(CoveredSize);
    private readonly BitArray _changed = new(CoveredSize);
    private readonly MultiAddressBreakPoint _armed;

    /// <summary>
    /// Creates the map and starts watching every image registered in <paramref name="registry"/>
    /// from now on.
    /// </summary>
    /// <param name="memory">The memory bus, read to compare the written value with the stored byte.</param>
    /// <param name="writeBreakpoints">The write breakpoint holder the armed breakpoint is installed into.</param>
    /// <param name="registry">The registry of loaded program images to watch.</param>
    public ChangedMemoryMap(Memory memory, AddressReadWriteBreakpoints writeBreakpoints,
        LoadedImageRegistry registry) {
        _memory = memory;
        _writeBreakpoints = writeBreakpoints;
        _armed = new MultiAddressBreakPoint(BreakPointType.MEMORY_WRITE, OnArmedWrite);
        registry.ImageLoaded += OnImageLoaded;
    }

    /// <summary>An immutable view of the map for the generator; empty when nothing was
    /// watched.</summary>
    internal ChangedMemorySnapshot Snapshot() {
        return new ChangedMemorySnapshot {
            WatchedAreas = ExportRuns(_watched),
            ChangedRanges = ExportRuns(_changed)
        };
    }

    /// <summary>Restores a snapshot: watches its areas again and marks its changed bytes. Used by
    /// the CFG graph reload so a reloaded run keeps the generator input of the dumped run.</summary>
    internal void Restore(ChangedMemorySnapshot snapshot) {
        foreach ((uint start, uint end) in snapshot.WatchedAreas) {
            Watch(start, end - start);
        }
        foreach ((uint start, uint end) in snapshot.ChangedRanges) {
            for (uint address = start; address < end && address < CoveredSize; address++) {
                _changed.Set((int)address, true);
                _writeBreakpoints.ToggleBreakPointAt(_armed, AddressOperation.WRITE, address, false);
            }
        }
    }

    /// <summary>True when <paramref name="address"/> is watched and not yet changed.</summary>
    internal bool IsArmed(uint address) =>
        address < CoveredSize && _watched.Get((int)address) && !_changed.Get((int)address);

    private void OnImageLoaded(object? sender, LoadedImage image) {
        Watch(image.StartAddress, image.SizeInBytes);
    }

    private void Watch(uint start, uint size) {
        uint end = Math.Min(start + size, CoveredSize);
        for (uint address = start; address < end; address++) {
            if (!_watched.Get((int)address)) {
                _watched.Set((int)address, true);
                if (!_changed.Get((int)address)) {
                    _writeBreakpoints.ToggleBreakPointAt(_armed, AddressOperation.WRITE, address, true);
                }
            }
        }
    }

    private void OnArmedWrite(BreakPoint breakPoint, long address) {
        MarkWhenDiffers((uint)address, _memory.CurrentlyWritingByte);
    }

    private void MarkWhenDiffers(uint address, byte value) {
        if (value != _memory.SneakilyRead(address)) {
            _changed.Set((int)address, true);
            _writeBreakpoints.ToggleBreakPointAt(_armed, AddressOperation.WRITE, address, false);
        }
    }

    private static IReadOnlyList<(uint Start, uint End)> ExportRuns(BitArray bits) {
        List<(uint Start, uint End)> ranges = [];
        uint rangeStart = 0;
        bool inRange = false;
        for (int address = 0; address < CoveredSize; address++) {
            bool set = bits.Get(address);
            if (set && !inRange) {
                rangeStart = (uint)address;
                inRange = true;
            } else if (!set && inRange) {
                ranges.Add((rangeStart, (uint)address));
                inRange = false;
            }
        }
        if (inRange) {
            ranges.Add((rangeStart, CoveredSize));
        }
        return ranges;
    }
}
