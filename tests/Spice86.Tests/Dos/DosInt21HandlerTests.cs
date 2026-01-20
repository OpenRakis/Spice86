namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.IOPorts;
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
        var dosFcbManager = new DosFcbManager(memory, dosFileManager, driveManager, logger);
        var recordingFile = new RecordingVirtualFile();
        const ushort fileHandle = 0x0003;
        dosFileManager.OpenFiles[fileHandle] = recordingFile;
        var ioPortBreakpoints = new Spice86.Core.Emulator.VM.Breakpoint.AddressReadWriteBreakpoints();
        var ioPortDispatcher = new IOPortDispatcher(ioPortBreakpoints, state, logger, false);
        var dosTables = new DosTables();
        dosTables.Initialize(memory);
        var dosSwappableDataArea = new DosSwappableDataArea(memory, 0xB20);
        var biosDataArea = new BiosDataArea(memory, 640);
        var biosKeyboardBuffer = new BiosKeyboardBuffer(memory, biosDataArea);
        var keyboardInt16Handler = new KeyboardInt16Handler(
            memory, ioPortDispatcher, biosDataArea,
            functionHandlerProvider, stack, state, logger, biosKeyboardBuffer);
        var countryInfo = new CountryInfo();
        var dosMemoryManager = new DosMemoryManager(memory, 0x170, logger);
        var envVars = new Dictionary<string, string> { { "PATH", "C:\\" } };
        var dosProcessManager = new DosProcessManager(memory, stack, state, dosMemoryManager, dosFileManager, driveManager, dosFcbManager, envVars, logger);

        var handler = new DosInt21Handler(
            memory,
            functionHandlerProvider,
            stack,
            state,
            keyboardInt16Handler,
            countryInfo,
            stringDecoder,
            dosMemoryManager,
            dosFileManager,
            driveManager,
            dosProcessManager,
            ioPortDispatcher,
            dosTables,
            logger,
            dosFcbManager);

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