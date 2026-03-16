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

    public virtual bool IsOnReadOnlyMedium { get; }

    public ushort Time { get; set; }

    public ushort Date { get; set; }

    public byte Flags { get; set; }

    public byte Drive { get; set; } = 0xff; //unset
    public ushort DeviceInformation { get; set; }
    public override string Name { get; set; }
    public override bool CanRead => _randomAccessStream.CanRead;
    public override bool CanSeek => _randomAccessStream.CanSeek;
    public override bool CanWrite => _randomAccessStream.CanWrite;
    public override long Length => _randomAccessStream.Length;

    public override long Position {
        get => _randomAccessStream.Position;
        set => _randomAccessStream.Position = value;
    }
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
