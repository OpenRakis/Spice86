namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using Xunit;

public class DosInt21HandlerTests {
    [Fact]
    public void MoveFilePointerUsingHandle_ShouldTreatCxDxOffsetAsSignedValue() {
        // Arrange
        IMemory memory = Substitute.For<IMemory>();
        var state = new State(CpuModel.INTEL_80286);
        var stack = new Stack(memory, state);
        ILoggerService logger = Substitute.For<ILoggerService>();
        IFunctionHandlerProvider functionHandlerProvider = Substitute.For<IFunctionHandlerProvider>();
        string cDrivePath = Path.GetTempPath();
        var driveManager = new DosDriveManager(logger, cDrivePath, null);
        var stringDecoder = new DosStringDecoder(memory, state);
        IList<IVirtualDevice> virtualDevices = new List<IVirtualDevice>();
        var dosFileManager = new DosFileManager(memory, stringDecoder, driveManager, logger, virtualDevices);
        var recordingFile = new RecordingVirtualFile();
        const ushort fileHandle = 0x0003;
        dosFileManager.OpenFiles[fileHandle] = recordingFile;

        var handler = new DosInt21Handler(
            memory,
            null!,
            functionHandlerProvider,
            stack,
            state,
            null!,
            null!,
            stringDecoder,
            null!,
            dosFileManager,
            driveManager,
            null!,  // ioPortDispatcher
            logger);

        state.AL = (byte)SeekOrigin.Current;
        state.BX = fileHandle;
        state.CX = 0xFFFF;
        state.DX = 0xFFFF;

        // Act
        handler.MoveFilePointerUsingHandle(false);

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