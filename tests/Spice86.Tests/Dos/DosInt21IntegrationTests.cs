namespace Spice86.Tests.Dos;

using FluentAssertions;
using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;
using Spice86.ViewModels.Services;

using System.Runtime.InteropServices;

using Xunit;

/// <summary>
/// Integration tests for DOS INT 21h functionality that run DOS COM resources through the emulation stack.
/// </summary>
public class DosInt21IntegrationTests {
    private const long KeyInjectionCycleCount = 50;
    private const long KeyboardTestInstructionsPerSecond = 1000;

    private enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    [Fact]
    public void GetDbcsLeadByteTable_WithAL0_ReturnsValidPointer() {
        AssertResourcePasses("dbcs_lead_byte_table_al0.com");
    }

    [Fact]
    public void GetDbcsLeadByteTable_WithInvalidAL_ReturnsError() {
        AssertResourcePasses("dbcs_lead_byte_table_invalid_al.com");
    }

    [Fact]
    public void CurrentDirectoryStructure_IsInitializedAtKnownLocation() {
        AssertResourcePasses("cds_initialized_known_location.com");
    }

    [Fact]
    public void DbcsTable_IsInPrivateTablesArea() {
        AssertResourcePasses("dbcs_table_private_area.com");
    }

    [Fact]
    public void GetPspAddress_ParentPspPointsToCommandCom() {
        AssertResourcePasses("psp_parent_command_com.com");
    }

    [Fact]
    public void EnvironmentBlock_NotCorruptedByPsp() {
        AssertResourcePasses("environment_block_not_corrupted.com");
    }

    [Fact]
    public void EnvironmentBlock_ContainsProgramPath() {
        AssertResourcePasses("environment_block_contains_program_path.com");
    }

    [Fact]
    public void GetChildReturnCode_ReturnsReturnCode() {
        AssertResourcePasses("child_return_code_initial.com");
    }

    [Fact]
    public void GetChildReturnCode_SubsequentCallsReturnZero() {
        AssertResourcePasses("child_return_code_subsequent_zero.com");
    }

    [Fact]
    public void Int20h_TerminatesProgramNormally() {
        AssertResourcePasses("int20_terminates.com");
    }

    [Fact]
    public void CreateNewPsp_CreatesValidPspCopy() {
        AssertResourcePasses("create_new_psp_valid_copy.com");
    }

    [Fact]
    public void CreateChildPsp_CreatesValidPsp() {
        AssertResourcePasses("create_child_psp_valid.com");
    }

    [Fact]
    public void ProgramArguments_CanAccessArgvFromEnvironmentBlock() {
        AssertResourcePasses("program_arguments_access_argv.com");
    }

    [Fact]
    public void ProgramArguments_CanPrintArgv0FromEnvironment() {
        AssertResourcePasses("program_arguments_print_argv0.com");
    }

    [Fact]
    public void StandardFileHandles_AreInheritedFromParentPsp() {
        TestIoPortHandler testHandler = RunDosResource("standard_file_handles_inherited.com");

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
        testHandler.Details.Take(3).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void SelectDefaultDrive_WithValidDrive_ReturnsMaxDriveCountAndSwitchesDrive() {
        AssertResourcePasses("select_default_drive.com");
    }

    [Fact]
    public void GetAllocationInfoForAnyDrive_WithDefaultDrive_ReturnsAllocationInfoAndMediaIdPointer() {
        AssertResourcePasses("allocation_info_default_drive.com");
    }

    [Fact]
    public void GetFileAttributes_ForReadOnlyFile_ReturnsDosAttributeBits() {
        string resourceName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "file_attributes_readonly.com"
            : "file_attributes_readonly_no_archive.com";

        TestIoPortHandler testHandler = RunDosResource(resourceName, fileSystemSetup: testRoot => {
            string testFilePath = Path.Join(testRoot, "TESTATTR.TXT");
            File.WriteAllText(testFilePath, "test");
            File.SetAttributes(testFilePath, FileAttributes.ReadOnly | FileAttributes.Archive);
        });

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    [Fact]
    public void SetFileAttributes_ThenGet_RoundTrips() {
        TestIoPortHandler testHandler = RunDosResource("set_file_attributes_roundtrip.com", fileSystemSetup: testRoot => {
            string testFilePath = Path.Join(testRoot, "SETATR.TXT");
            File.WriteAllText(testFilePath, "test");
            File.SetAttributes(testFilePath, FileAttributes.Archive);
        });

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    [Fact]
    public void GetSetControlBreak_RoundTrips() {
        AssertResourcePasses("control_break_roundtrip.com");
    }

    [Fact]
    public void CheckStandardInputStatus_WithBreakEnabled_DetectsCtrlCAndInvokesInt23h() {
        TestIoPortHandler testHandler = RunDosResource("check_stdin_status_ctrl_break.com", keyInjectionAction: SimulateCtrlC);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    [Fact]
    public void CheckStandardInputStatus_WithBreakEnabled_DetectsCtrlBreakAndInvokesInt23h() {
        TestIoPortHandler testHandler = RunDosResource("check_stdin_status_ctrl_break.com", keyInjectionAction: SimulateCtrlBreak);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    [Fact]
    public void AH07h_ReadsFromStdinHandle_NotDirectlyFromInt16h() {
        TestIoPortHandler testHandler = RunDosResource("stdin_read_ah07.com", preRunSetup: RedirectStdinToByteA);

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "AH=07h should read 0x41 from the redirected STDIN handle");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    [Fact]
    public void AH08h_ReadsFromStdinHandle_NotDirectlyFromInt16h() {
        TestIoPortHandler testHandler = RunDosResource("stdin_read_ah08.com", preRunSetup: RedirectStdinToByteA);

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "AH=08h should read 0x41 from the redirected STDIN handle");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    [Fact]
    public void AH01h_ReadsFromStdinHandle_NotDirectlyFromInt16h() {
        TestIoPortHandler testHandler = RunDosResource("stdin_read_ah01.com", preRunSetup: RedirectStdinToByteA);

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "AH=01h should read 0x41 from the redirected STDIN handle");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    [Fact]
    public void AH0Ah_ReadsFromStdinHandle_NotDirectlyFromInt16h() {
        TestIoPortHandler testHandler = RunDosResource("stdin_buffered_read_ah0a.com", preRunSetup: RedirectStdinToHelloCr);

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "AH=0Ah should read from the redirected STDIN handle");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    [Fact]
    public void KeyboardReassignmentReset_InstallsCtrlPrintScreenDefault() {
        TestIoPortHandler testHandler = RunDosResource("keyboard_reassignment_reset.com");

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "program should complete without hanging");
        testHandler.Details.Should().HaveCount(1,
            "should read exactly one character from Ctrl+PrintScreen");
        testHandler.Details[0].Should().Be(0x10,
            "Ctrl+PrintScreen should be translated to Ctrl+P (0x10) by default key mapping");
    }

    [Fact]
    public void FlushInput_ClearsStuffaheadBuffer() {
        TestIoPortHandler testHandler = RunDosResource("flush_input_clears_stuffahead.com");

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "program should complete without hanging");
        testHandler.Details.Should().HaveCount(1,
            "should read exactly one character after flush");
        testHandler.Details[0].Should().Be((byte)'Z',
            "flush should have discarded the DSR response; next read should get 'Z' from keyboard");
    }

    [Fact]
    public void DeviceStatusReport_ReturnsCorrectCursorPosition() {
        byte[] expectedResponse = { 0x1B, (byte)'[', (byte)'5', (byte)';', (byte)'1', (byte)'0', (byte)'R', 0x0D };

        TestIoPortHandler testHandler = RunDosResource("device_status_report.com");

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "program should complete without hanging");
        testHandler.Details.Should().HaveCount(expectedResponse.Length,
            "DSR response should be exactly 8 bytes");
        testHandler.Details.Should().Equal(expectedResponse,
            "DSR response should be ESC[5;10R\\r for cursor at row 4, col 9");
    }

    private static void AssertResourcePasses(string resourceName) {
        TestIoPortHandler testHandler = RunDosResource(resourceName);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    private static TestIoPortHandler RunDosResource(string resourceName,
        Action<HeadlessGui>? keyInjectionAction = null,
        Action<Spice86DependencyInjection>? preRunSetup = null,
        Action<string>? fileSystemSetup = null) {
        string resourcePath = Path.Join(AppContext.BaseDirectory, "Resources", "DosInt21Tests", resourceName);
        if (!string.Equals(Path.GetExtension(resourcePath), ".com", StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("DOS INT 21h resource tests require a DOS COM program.", nameof(resourceName));
        }

        using TempFile? tempFile = fileSystemSetup is null ? null : new TempFile($"DosInt21IntegrationTests_{Path.GetFileNameWithoutExtension(resourceName)}");
        string executablePath = resourcePath;
        string cDrive = Directory.GetParent(resourcePath)?.FullName ?? AppContext.BaseDirectory;
        if (tempFile is not null) {
            fileSystemSetup?.Invoke(tempFile.Path);
            executablePath = tempFile.CreateFile("TEST.COM", File.ReadAllBytes(resourcePath));
            cDrive = tempFile.Path;
        }

        long? instructionsPerSecond = keyInjectionAction is not null
            ? KeyboardTestInstructionsPerSecond
            : null;

        using Spice86Creator creator = new(
            binName: executablePath,
            enablePit: false,
            maxCycles: 100000L,
            installInterruptVectors: true,
            enableA20Gate: true,
            cDrive: cDrive,
            instructionTimeScale: instructionsPerSecond
        );
        using Spice86DependencyInjection spice86DependencyInjection = creator.Create();

        if (keyInjectionAction is not null) {
            HeadlessGui? headlessGui = spice86DependencyInjection.HeadlessGui;
            if (headlessGui is null) {
                throw new InvalidOperationException(
                    "HeadlessGui is not available - keyboard injection requires HeadlessType.Minimal");
            }
            spice86DependencyInjection.Machine.EmulatorBreakpointsManager.ToggleBreakPoint(
                new AddressBreakPoint(BreakPointType.CPU_CYCLES, KeyInjectionCycleCount,
                    _ => keyInjectionAction(headlessGui), isRemovedOnTrigger: true), true);
        }

        preRunSetup?.Invoke(spice86DependencyInjection);

        TestIoPortHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    private static void RedirectStdinToByteA(Spice86DependencyInjection di) {
        MemoryStream stream = new(new byte[] { 0x41 });
        DosFile stdinFile = new("STDIN", 0, stream);
        di.Machine.Dos.FileManager.OpenFiles[0] = stdinFile;
    }

    private static void RedirectStdinToHelloCr(Spice86DependencyInjection di) {
        byte[] data = { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x0D };
        MemoryStream stream = new(data);
        DosFile stdinFile = new("STDIN", 0, stream);
        di.Machine.Dos.FileManager.OpenFiles[0] = stdinFile;
    }

    private static void SimulateCtrlC(HeadlessGui gui) {
        gui.SimulateKeyPress(PhysicalKey.ControlLeft);
        gui.SimulateKeyPress(PhysicalKey.C);
        gui.SimulateKeyRelease(PhysicalKey.C);
        gui.SimulateKeyRelease(PhysicalKey.ControlLeft);
    }

    private static void SimulateCtrlBreak(HeadlessGui gui) {
        gui.SimulateKeyPress(PhysicalKey.ControlLeft);
        gui.SimulateKeyPress(PhysicalKey.Pause);
        gui.SimulateKeyRelease(PhysicalKey.ControlLeft);
    }

}
