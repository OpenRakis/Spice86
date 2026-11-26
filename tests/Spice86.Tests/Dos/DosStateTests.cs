namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Tests.Utility;

using System.Text;
using System.Text.Json;

using Xunit;

/// <summary>
/// Checks <see cref="Dos.CaptureState" /> and <see cref="Dos.RestoreState" />: capture, change the DOS state
/// through INT 21h services, restore, and compare what the services return with what they returned at capture.
/// </summary>
public sealed class DosStateTests {
    private const ushort CapturedDtaSegment = 0x2000;
    private const ushort CapturedDtaOffset = 0x0080;
    private const ushort OtherDtaSegment = 0x3000;
    private const ushort OtherDtaOffset = 0x0100;
    private const ushort BestFit = 0x0001;
    private const ushort LastFit = 0x0002;
    private const ushort ChildReturnCode = 0x0105;
    private const byte DriveC = 2;
    private const byte DriveZ = 25;
    private const byte GetCtrlCFlag = 0;
    private const byte SetCtrlCFlag = 1;
    private const byte QueryCodePage = 1;
    private const byte SetCodePage = 2;
    private const ushort UsCodePage = 437;
    private const ushort MultilingualCodePage = 850;
    private const uint DirectoryBufferAddress = 0x40000;
    private const uint ConsoleBufferAddress = 0x41000;
    private const ushort StdinHandle = 0;
    private const ushort StdoutHandle = 1;
    private const uint TextVideoMemory = 0xB8000;
    private const uint BiosCursorPositionPage0 = 0x450;
    private const uint BiosScreenColumns = 0x44A;
    private const byte RedOnBlack = 0x04;

    [Fact]
    public void RestoreAfterChangesThroughServicesGivesTheServicesAnswersAtCapture() {
        // Arrange
        using TempFile directory = CreateFolderWithFiles();
        using DosTestFixture fixture = new(directory.Path);
        ArrangeCapturedState(fixture);
        DosState captured = fixture.Dos.CaptureState();
        ServiceAnswers atCapture = ReadServiceAnswers(fixture);

        // Act
        ChangeStateThroughServices(fixture, atCapture.CodePage);
        fixture.Dos.RestoreState(captured);

        // Assert
        ServiceAnswers restored = ReadServiceAnswers(fixture);
        restored.Should().Be(atCapture);
    }

    [Fact]
    public void RestoringOneStateTwiceGivesTheSameAnswersEachTime() {
        // Arrange
        using TempFile directory = CreateFolderWithFiles();
        using DosTestFixture fixture = new(directory.Path);
        ArrangeCapturedState(fixture);
        DosState captured = fixture.Dos.CaptureState();
        ServiceAnswers atCapture = ReadServiceAnswers(fixture);

        // Act
        ChangeStateThroughServices(fixture, atCapture.CodePage);
        fixture.Dos.RestoreState(captured);
        ServiceAnswers first = ReadServiceAnswers(fixture);
        ChangeStateThroughServices(fixture, atCapture.CodePage);
        fixture.Dos.RestoreState(captured);
        ServiceAnswers second = ReadServiceAnswers(fixture);

        // Assert
        first.Should().Be(atCapture);
        second.Should().Be(atCapture);
    }

    [Fact]
    public void StateRestoresTheSameAfterAJsonRoundTrip() {
        // Arrange
        using TempFile directory = CreateFolderWithFiles();
        using DosTestFixture fixture = new(directory.Path);
        ArrangeCapturedState(fixture);
        string json = JsonSerializer.Serialize(fixture.Dos.CaptureState());
        ServiceAnswers atCapture = ReadServiceAnswers(fixture);

        // Act
        DosState deserialized = JsonSerializer.Deserialize<DosState>(json)
            ?? throw new InvalidOperationException("The state did not deserialize.");
        ChangeStateThroughServices(fixture, atCapture.CodePage);
        fixture.Dos.RestoreState(deserialized);

        // Assert
        ReadServiceAnswers(fixture).Should().Be(atCapture);
    }

    [Fact]
    public void RestoreInTheMiddleOfAnAnsiSequenceFinishesTheCapturedSequence() {
        // Arrange: the console has read ESC [ 3 of the sequence ESC [ 3 1 m (red text)
        using TempFile directory = CreateFolderWithFiles();
        using DosTestFixture fixture = new(directory.Path);
        WriteToStdout(fixture, "\u001b[3");
        DosState captured = fixture.Dos.CaptureState();

        // Act: another sequence sets green, then the restored console reads the end of the red one
        WriteToStdout(fixture, "2mG");
        fixture.Dos.RestoreState(captured);
        WriteToStdout(fixture, "1m");
        uint attributeAddress = AttributeAddressAtCursor(fixture);
        WriteToStdout(fixture, "R");

        // Assert
        fixture.Memory.UInt8[attributeAddress].Should().Be(RedOnBlack);
    }

    [Fact]
    public void RestoreGivesBackThePendingConsoleInputAndTheKeyReassignments() {
        // Arrange: A is reassigned to B, and a cursor position report waits to be read from CON
        using TempFile directory = CreateFolderWithFiles();
        using DosTestFixture fixture = new(directory.Path);
        WriteToStdout(fixture, "\u001b[65;66p\u001b[6n");
        DosState captured = fixture.Dos.CaptureState();
        ushort pendingLength = (ushort)captured.Console.StuffAheadBuffer.Length;
        byte[] readAtCapture = ReadFromStdin(fixture, pendingLength);

        // Act: the reassignment is removed, then the state is restored
        WriteToStdout(fixture, "\u001b[65p");
        fixture.Dos.RestoreState(captured);

        // Assert
        pendingLength.Should().BeGreaterThan(0);
        ReadFromStdin(fixture, pendingLength).Should().Equal(readAtCapture);
        ConsoleDeviceState console = fixture.Dos.CaptureState().Console;
        console.KeyRedefinitions.Select(static key => (key.KeyCode, key.Replacement.ToArray()[0]))
            .Should().Equal(((ushort)'A', (byte)'B'));
    }

    [Fact]
    public void RestoreOfAStateWithAnUnmountedDriveThrowsAndChangesNothing() {
        // Arrange
        using TempFile directory = CreateFolderWithFiles();
        using DosTestFixture fixture = new(directory.Path);
        ArrangeCapturedState(fixture);
        DosState unmountedDrive = fixture.Dos.CaptureState() with { CurrentDrive = 'Q' };
        ChangeStateThroughServices(fixture, fixture.DosInt21Handler.GetSetCodePage(QueryCodePage, 0, 0).bx);
        ServiceAnswers beforeRestore = ReadServiceAnswers(fixture);

        // Act
        Action restore = () => fixture.Dos.RestoreState(unmountedDrive);

        // Assert: each read of the answers moves the search on, so the search is left out of the comparison
        restore.Should().Throw<KeyNotFoundException>();
        ServiceAnswers afterRestore = ReadServiceAnswers(fixture);
        afterRestore.Should().Be(beforeRestore with { NextFile = afterRestore.NextFile });
    }

    [Fact]
    public void RestoreOfAStateWithARefusedCodePageThrowsAndChangesNothing() {
        // Arrange
        using TempFile directory = CreateFolderWithFiles();
        using DosTestFixture fixture = new(directory.Path);
        ArrangeCapturedState(fixture);
        DosState refusedCodePage = fixture.Dos.CaptureState() with { ActiveCodePage = 1 };
        ChangeStateThroughServices(fixture, fixture.DosInt21Handler.GetSetCodePage(QueryCodePage, 0, 0).bx);
        ServiceAnswers beforeRestore = ReadServiceAnswers(fixture);

        // Act
        Action restore = () => fixture.Dos.RestoreState(refusedCodePage);

        // Assert: each read of the answers moves the search on, so the search is left out of the comparison
        restore.Should().Throw<InvalidOperationException>();
        ServiceAnswers afterRestore = ReadServiceAnswers(fixture);
        afterRestore.Should().Be(beforeRestore with { NextFile = afterRestore.NextFile });
    }

    private static TempFile CreateFolderWithFiles() {
        TempFile directory = new("Spice86_DosState_Tests");
        string subFolder = Path.Join(directory.Path, "SUB");
        Directory.CreateDirectory(subFolder);
        foreach (string name in new[] { "A.TXT", "B.TXT", "C.TXT", "D.TXT" }) {
            File.WriteAllText(Path.Join(subFolder, name), name);
        }
        return directory;
    }

    /// <summary>
    /// Puts DOS in a state that differs from its start in every captured item, and starts a search.
    /// </summary>
    private static void ArrangeCapturedState(DosTestFixture fixture) {
        DosInt21Handler dos = fixture.DosInt21Handler;
        dos.SetDiskTransferAddress(CapturedDtaSegment, CapturedDtaOffset);
        dos.ChangeDirectory("SUB").IsError.Should().BeFalse();
        dos.MemoryAllocationStrategy(0x5801, BestFit).carryFlag.Should().BeFalse();
        dos.GetSetControlBreak(SetCtrlCFlag, 1, 0, 0);
        fixture.ProcessManager.LastChildReturnCode = ChildReturnCode;
        fixture.DosFileManager.FindFirstMatchingFile("*.TXT", 0).IsError.Should().BeFalse();
    }

    private static void ChangeStateThroughServices(DosTestFixture fixture, ushort capturedCodePage) {
        DosInt21Handler dos = fixture.DosInt21Handler;
        fixture.DosFileManager.FindNextMatchingFile().IsError.Should().BeFalse();
        dos.SetDiskTransferAddress(OtherDtaSegment, OtherDtaOffset);
        dos.ChangeDirectory("\\").IsError.Should().BeFalse();
        dos.SelectDefaultDrive(DriveZ);
        dos.MemoryAllocationStrategy(0x5801, LastFit).carryFlag.Should().BeFalse();
        dos.GetSetControlBreak(SetCtrlCFlag, 0, 0, 0);
        dos.ReadReturnCode();
        ushort otherCodePage = capturedCodePage == UsCodePage ? MultilingualCodePage : UsCodePage;
        dos.GetSetCodePage(SetCodePage, otherCodePage, 0xFFFF).result.IsError.Should().BeFalse();
    }

    /// <summary>
    /// Reads every captured item through its INT 21h service. Get return code clears the code and find next
    /// moves the search on, so the answers can be read once per capture or restore.
    /// </summary>
    private static ServiceAnswers ReadServiceAnswers(DosTestFixture fixture) {
        DosInt21Handler dos = fixture.DosInt21Handler;
        ushort dtaSegment = fixture.DosFileManager.DiskTransferAreaAddressSegment;
        ushort dtaOffset = fixture.DosFileManager.DiskTransferAreaAddressOffset;
        byte drive = dos.ReadCurrentDefaultDrive();
        dos.GetCurrentDirectory(DriveC + 1, (ushort)(DirectoryBufferAddress >> 4), 0).IsError.Should().BeFalse();
        string directory = fixture.Memory.GetZeroTerminatedString(DirectoryBufferAddress, 64);
        ushort strategy = dos.MemoryAllocationStrategy(0x5800, 0).ax;
        byte ctrlC = (byte)dos.GetSetControlBreak(GetCtrlCFlag, 0, 0, 0).dx;
        ushort codePage = dos.GetSetCodePage(QueryCodePage, 0, 0).bx;
        ushort returnCode = dos.ReadReturnCode();
        DosFileOperationResult next = fixture.DosFileManager.FindNextMatchingFile();
        string nextFile = next.IsError ? "" : fixture.DosFileManager.DiskTransferArea.FileName;
        return new ServiceAnswers(dtaSegment, dtaOffset, drive, directory, strategy, ctrlC, codePage, returnCode,
            nextFile);
    }

    private static void WriteToStdout(DosTestFixture fixture, string text) {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        fixture.Memory.LoadData(ConsoleBufferAddress, bytes);
        fixture.DosFileManager.WriteToFileOrDevice(StdoutHandle, (ushort)bytes.Length, ConsoleBufferAddress)
            .IsError.Should().BeFalse();
    }

    private static byte[] ReadFromStdin(DosTestFixture fixture, ushort length) {
        DosFileOperationResult result = fixture.DosFileManager.ReadFileOrDevice(StdinHandle, length,
            ConsoleBufferAddress);
        result.IsError.Should().BeFalse();
        return fixture.Memory.ReadRam(length, ConsoleBufferAddress);
    }

    private static uint AttributeAddressAtCursor(DosTestFixture fixture) {
        ushort cursor = fixture.Memory.UInt16[BiosCursorPositionPage0];
        uint row = (uint)(cursor >> 8);
        uint column = (uint)(cursor & 0xFF);
        uint columns = fixture.Memory.UInt16[BiosScreenColumns];
        return TextVideoMemory + ((row * columns) + column) * 2 + 1;
    }

    private readonly record struct ServiceAnswers(
        ushort DtaSegment,
        ushort DtaOffset,
        byte Drive,
        string Directory,
        ushort Strategy,
        byte CtrlC,
        ushort CodePage,
        ushort ReturnCode,
        string NextFile);
}
