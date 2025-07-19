namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// Represents a file that has been opened by DOS.
/// </summary>
public class DosFile : VirtualFileBase {
    private readonly int _descriptor;
    private readonly List<MemoryRange> _loadedMemoryRanges = new();
    private readonly Stream _randomAccessStream;

    /// <summary>
    /// Initializes a new instance of the <see cref="DosFile"/> class.
    /// </summary>
    /// <param name="name">The name of the file.</param>
    /// <param name="descriptor">The file descriptor used by DOS.</param>
    /// <param name="randomAccessFile">The stream used for random access to the file.</param>
    public DosFile(string name, int descriptor, Stream randomAccessFile) {
        Name = name;
        _descriptor = descriptor;
        _randomAccessStream = randomAccessFile;
    }

    /// <summary>
    /// Adds a memory range to the list of loaded memory ranges for the file.
    /// </summary>
    /// <param name="newMemoryRange">The memory range to add.</param>
    public void AddMemoryRange(MemoryRange newMemoryRange) {
        for (int i = 0; i < _loadedMemoryRanges.Count; i++) {
            MemoryRange existingMemoryRange = _loadedMemoryRanges[i];
            if (existingMemoryRange.StartAddress == newMemoryRange.StartAddress &&
                existingMemoryRange.EndAddress == newMemoryRange.EndAddress) {
                // Same, nothing to do
                return;
            }

            if (existingMemoryRange.IsInRange(newMemoryRange.StartAddress, newMemoryRange.EndAddress)) {
                // Fuse
                existingMemoryRange.StartAddress = Math.Min(existingMemoryRange.StartAddress, newMemoryRange.StartAddress);
                existingMemoryRange.EndAddress = Math.Max(existingMemoryRange.EndAddress, newMemoryRange.EndAddress);
                return;
            }

            if (existingMemoryRange.EndAddress + 1 == newMemoryRange.StartAddress) {
                // We are the next block, extend
                existingMemoryRange.EndAddress = newMemoryRange.EndAddress;
                return;
            }

            if (existingMemoryRange.StartAddress - 1 == newMemoryRange.EndAddress) {
                // The new range comes immediately before this existing range, extend
                existingMemoryRange.StartAddress = newMemoryRange.StartAddress;
                return;
            }
        }

        _loadedMemoryRanges.Add(newMemoryRange);
    }

    /// <summary>
    /// Gets the file descriptor used by DOS.
    /// </summary>
    public int Descriptor => _descriptor;

    /// <summary>
    /// Gets a list of memory ranges that have been loaded for the file.
    /// </summary>
    public IList<MemoryRange> LoadedMemoryRanges => _loadedMemoryRanges;

    public virtual bool IsOnReadOnlyMedium { get; }

    public ushort Time { get; set; }

    public ushort Date { get; set; }

    public byte Flags { get; set; }

    public byte Drive { get; set; } = 0xff; //unset
    public override string Name { get; set; }
    public override bool CanRead { get; }
    public override bool CanSeek { get; }
    public override bool CanWrite { get; }
    public override long Length { get; }
    public override long Position { get; set; }
    /// <summary>
    /// Closes the file.
    /// </summary>
    public override void Close() {
        _randomAccessStream.Close();
    }

    public override void Flush() {
        _randomAccessStream.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count) {
        return _randomAccessStream.Read(buffer, offset, count);
    }

    public override void SetLength(long value) {
        _randomAccessStream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count) {
        _randomAccessStream.Write(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin) {
        return _randomAccessStream.Seek(offset, origin);
    }
}
