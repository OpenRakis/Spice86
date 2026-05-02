namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using Xunit;

public class DosInt21HandlerTests : IDisposable {
    private readonly DosTestFixture _fixture = new(Path.GetTempPath());

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void MoveFilePointerUsingHandle_ShouldTreatCxDxOffsetAsSignedValue() {
        // Arrange
        RecordingVirtualFile recordingFile = new();
        const ushort fileHandle = 0x0003;
        _fixture.DosFileManager.OpenFiles[fileHandle] = recordingFile;

        _fixture.CpuState.AL = (byte)SeekOrigin.Current;
        _fixture.CpuState.BX = fileHandle;
        _fixture.CpuState.CX = 0xFFFF;
        _fixture.CpuState.DX = 0xFFFF;

        // Act
        _fixture.DosInt21Handler.MoveFilePointerUsingHandle(false);

        // Assert
        recordingFile.LastSeekOffset.Should().Be(-1);
        recordingFile.LastSeekOrigin.Should().Be(SeekOrigin.Current);
    }

    private sealed class RecordingVirtualFile : VirtualFileBase {
        private long _length;

        public long LastSeekOffset { get; private set; }

        public SeekOrigin LastSeekOrigin { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position { get; set; }

        public override void Flush() {
        }

        public override int Read(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin) {
            LastSeekOffset = offset;
            LastSeekOrigin = origin;
            return origin switch {
                SeekOrigin.Begin => Position = offset,
                SeekOrigin.Current => Position += offset,
                SeekOrigin.End => Position = _length + offset,
                _ => Position
            };
        }

        public override void SetLength(long value) {
            _length = value;
        }

        public override void Write(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }
    }
}