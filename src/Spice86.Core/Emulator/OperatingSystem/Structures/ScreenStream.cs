namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.VM;

using System.Linq;

/// <summary>
///     Represents a stream for writing to the screen.
/// </summary>
public class ScreenStream : Stream {
    private readonly Machine _machine;

    /// <summary>
    ///     Creates a new instance of the <see cref="ScreenStream" /> class.
    /// </summary>
    /// <param name="machine"></param>
    public ScreenStream(Machine machine) {
        _machine = machine;
    }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => -1;

    /// <inheritdoc />
    public override long Position { get; set; }

    /// <inheritdoc />
    public override void Flush() {
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) {
        throw new NotSupportedException("Cannot read from the screen.");
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) {
        throw new NotSupportedException("Cannot seek in the screen.");
    }

    /// <inheritdoc />
    public override void SetLength(long value) {
        throw new NotSupportedException("Cannot set the length of the screen.");
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) {
        byte[] bytesToWrite = buffer.Skip(offset).Take(count).ToArray();
        byte originalAl = _machine.Cpu.State.AL;
        foreach (byte character in bytesToWrite) {
            _machine.Cpu.State.AL = character;
            _machine.VideoBiosInt10Handler.WriteTextInTeletypeMode();
        }
        _machine.Cpu.State.AL = originalAl;
    }
}