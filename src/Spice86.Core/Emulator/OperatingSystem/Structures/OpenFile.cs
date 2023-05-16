namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// Represents a file that has been opened by DOS.
/// </summary>
public class OpenFile {
    private readonly int _descriptor;
    private readonly List<MemoryRange> _loadedMemoryRanges = new();
    private readonly string _name;
    private readonly Stream _randomAccessFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenFile"/> class.
    /// </summary>
    /// <param name="name">The name of the file.</param>
    /// <param name="descriptor">The file descriptor used by DOS.</param>
    /// <param name="randomAccessFile">The stream used for random access to the file.</param>
    public OpenFile(string name, int descriptor, Stream randomAccessFile) {
        _name = name;
        _descriptor = descriptor;
        _randomAccessFile = randomAccessFile;
    }

    /// <summary>
    /// Adds a memory range to the list of loaded memory ranges for the file.
    /// </summary>
    /// <param name="memoryRange">The memory range to add.</param>
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

    /// <summary>
    /// Gets the file descriptor used by DOS.
    /// </summary>
    public int Descriptor => _descriptor;

    /// <summary>
    /// Gets a list of memory ranges that have been loaded for the file.
    /// </summary>
    public IList<MemoryRange> LoadedMemoryRanges => _loadedMemoryRanges;

    /// <summary>
    /// Gets the name of the file.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the stream used for random access to the file.
    /// </summary>
    public Stream RandomAccessFile => _randomAccessFile;
}