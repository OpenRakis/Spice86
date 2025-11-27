namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using Xunit;

public class DosInt21HandlerTests {
    [Fact]
    public void MoveFilePointerUsingHandle_ShouldTreatCxDxOffsetAsSignedValue() {
        // Arrange
        IMemory memory = Substitute.For<IMemory>();
        State state = new(CpuModel.INTEL_80286);
        Stack stack = new(memory, state);
        ILoggerService logger = Substitute.For<ILoggerService>();
        IFunctionHandlerProvider functionHandlerProvider = Substitute.For<IFunctionHandlerProvider>();
        string cDrivePath = Path.GetTempPath();
        string executablePath = Path.Combine(cDrivePath, "test.exe");
        DosDriveManager driveManager = new(logger, cDrivePath, executablePath);
        DosStringDecoder stringDecoder = new(memory, state);
        IList<IVirtualDevice> virtualDevices = new List<IVirtualDevice>();
        DosFileManager dosFileManager = new(memory, stringDecoder, driveManager, logger, virtualDevices);
        RecordingVirtualFile recordingFile = new();
        const ushort fileHandle = 0x0003;
        dosFileManager.OpenFiles[fileHandle] = recordingFile;

        // Create minimal real instances for unused dependencies
        Configuration configuration = new();
        DosProgramSegmentPrefixTracker dosPspTracker = new(configuration, memory, logger);
        DosMemoryManager dosMemoryManager = new(memory, dosPspTracker, logger);
        BiosDataArea biosDataArea = new(memory, 640);  // 640KB conventional memory
        BiosKeyboardBuffer biosKeyboardBuffer = new(memory, biosDataArea);
        KeyboardInt16Handler keyboardHandler = new(memory, biosDataArea, functionHandlerProvider, stack, state, logger, biosKeyboardBuffer);
        CountryInfo countryInfo = new();
        DosTables dosTables = new();
        DosProcessManager dosProcessManager = new(memory, state, dosPspTracker, dosMemoryManager, 
            dosFileManager, driveManager, new Dictionary<string, string>(), logger);
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, logger, false);

        DosInt21Handler handler = new(
            memory,
            dosPspTracker,
            functionHandlerProvider,
            stack,
            state,
            keyboardHandler,
            countryInfo,
            stringDecoder,
            dosMemoryManager,
            dosFileManager,
            driveManager,
            dosProcessManager,
            ioPortDispatcher,
            dosTables,
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