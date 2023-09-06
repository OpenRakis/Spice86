namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

using System.Linq;

/// <summary>
///     Represents a stream for writing to the screen.
/// </summary>
public class ScreenStream : Stream {
    private readonly State _state;
    private readonly IVgaFunctionality _vgaFunctionality;
    
    /// <summary>
    ///     Creates a new instance of the <see cref="ScreenStream" /> class.
    /// </summary>
    public ScreenStream(State state, IVgaFunctionality vgaFunctionality) {
        _state = state;
        _vgaFunctionality = vgaFunctionality;
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
        byte originalAl = _state.AL;
        foreach (byte character in bytesToWrite) {
            _vgaFunctionality.WriteTextInTeletypeMode(new CharacterPlusAttribute((char)character, 0x07, false));
        }
        _state.AL = originalAl;
    }
}