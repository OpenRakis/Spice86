namespace Spice86.Emulator.InterruptHandlers.Dos;

using Spice86.Emulator.Memory;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Represents a file opened by DOS.
/// </summary>
public class OpenFile {
    private readonly int _descriptor;
    private readonly List<MemoryRange> _loadMemoryRanges = new();
    private readonly string _name;
    private readonly FileStream _randomAccessFile;

    public OpenFile(string name, int descriptor, FileStream randomAccessFile) {
        _name = name;
        _descriptor = descriptor;
        _randomAccessFile = randomAccessFile;
    }

    public void AddMemoryRange(MemoryRange memoryRange) {
        foreach (MemoryRange loadMemoryRange in _loadMemoryRanges) {
            if (loadMemoryRange.GetStartAddress() == memoryRange.GetStartAddress() && loadMemoryRange.GetEndAddress() == memoryRange.GetEndAddress()) {
                // Same, nothing to do
                return;
            }

            if (loadMemoryRange.IsInRange(memoryRange.GetStartAddress(), memoryRange.GetEndAddress())) {
                // Fuse
                loadMemoryRange.SetStartAddress(Math.Min(loadMemoryRange.GetStartAddress(), memoryRange.GetStartAddress()));
                loadMemoryRange.SetEndAddress(Math.Max(loadMemoryRange.GetEndAddress(), memoryRange.GetEndAddress()));
                return;
            }

            if (loadMemoryRange.GetEndAddress() + 1 == memoryRange.GetStartAddress()) {
                // We are the next block, extend
                loadMemoryRange.SetEndAddress(memoryRange.GetEndAddress());
                return;
            }
        }

        _loadMemoryRanges.Add(memoryRange);
    }

    public int GetDescriptor() {
        return _descriptor;
    }

    public IList<MemoryRange> GetLoadMemoryRanges() {
        return _loadMemoryRanges;
    }

    public string GetName() {
        return _name;
    }

    public FileStream GetRandomAccessFile() {
        return _randomAccessFile;
    }
}