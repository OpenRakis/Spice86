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
    private readonly List<MemoryRange> _loadedMemoryRanges = new();
    private readonly string _name;
    private readonly FileStream _randomAccessFile;

    public OpenFile(string name, int descriptor, FileStream randomAccessFile) {
        _name = name;
        _descriptor = descriptor;
        _randomAccessFile = randomAccessFile;
    }

    public void AddMemoryRange(MemoryRange memoryRange) {
        for (int i = 0; i < _loadedMemoryRanges.Count; i++) {
            MemoryRange loadMemoryRange = _loadedMemoryRanges[i];
            if (loadMemoryRange.StartAddress == memoryRange.StartAddress && loadMemoryRange.EndAddress == memoryRange.EndAddress) {
                // Same, nothing to do
                return;
            }

            if (loadMemoryRange.IsInRange(memoryRange.StartAddress, memoryRange.EndAddress)) {
                // Fuse
                loadMemoryRange.StartAddress = Math.Min(loadMemoryRange.StartAddress, memoryRange.StartAddress);
                loadMemoryRange.EndAddress = Math.Max(loadMemoryRange.EndAddress, memoryRange.EndAddress);
                return;
            }

            if (loadMemoryRange.EndAddress + 1 == memoryRange.StartAddress) {
                // We are the next block, extend
                loadMemoryRange.EndAddress = memoryRange.EndAddress;
                return;
            }
        }

        _loadedMemoryRanges.Add(memoryRange);
    }

    public int Descriptor => _descriptor;

    public IList<MemoryRange> LoadedMemoryRanges => _loadedMemoryRanges;

    public string Name => _name;

    public FileStream RandomAccessFile => _randomAccessFile;
}