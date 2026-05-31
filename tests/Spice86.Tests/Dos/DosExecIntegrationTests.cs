namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;
using Spice86.Tests.Utility;

using System;
using System.IO;
using System.Text;

using Xunit;

public class DosExecIntegrationTests {
    [Fact]
    public void ExecModesAndOverlays_ShouldReportSuccessViaVideoMemory() {
        // Arrange
        using TempFile tempFile = new("dos_exec");
        CopyResourceFiles("DosExecTests", tempFile.Path,
            new[] { "dos_exec_master.com", "child.com", "tsr_hook.com", "overlay_driver.bin" });
        File.Move(Path.Join(tempFile.Path, "overlay_driver.bin"), Path.Join(tempFile.Path, "dos_exec_master.000"));

        // Act
        // The bootstrap autoexec line "CALL <program>" is echoed at the top of the
        // screen (MS-DOS / dosbox-staging parity), so the program output appears
        // after it. Scan the full text-mode screen for the expected token.
        string screen = RunProgramAndReadVideoOutput(
            Path.Join(tempFile.Path, "dos_exec_master.com"), tempFile.Path, expectedLength: 80 * 25);

        // Assert
        screen.Should().Contain("SEMJCTLOAV");
    }

    [Fact]
    public void FcbProcessIsolation_ParentFcbSurvivesChildTermination() {
        // Arrange
        using TempFile tempFile = new("dos_fcb");
        CopyResourceFiles("DosFcbTests", tempFile.Path,
            new[] { "fcb_process_isolation.com", "fcbchild.com" });

        // Act
        // The bootstrap autoexec line "CALL <program>" is echoed at the top of the
        // screen (MS-DOS / dosbox-staging parity), so the program output appears
        // after it. Scan the full text-mode screen for the expected token.
        string screen = RunProgramAndReadVideoOutput(
            Path.Join(tempFile.Path, "fcb_process_isolation.com"), tempFile.Path, expectedLength: 80 * 25);

        // Assert
        screen.Should().Contain("SCWOXER");
    }

    private static void CopyResourceFiles(string resourceSubDir, string tempDir, string[] files) {
        string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", resourceSubDir);
        foreach (string file in files) {
            File.Copy(Path.Join(resourceDir, file), Path.Join(tempDir, file), overwrite: true);
        }
    }

    private static string RunProgramAndReadVideoOutput(string programPath, string cDrive, int expectedLength) {
        using Spice86Creator creator = new Spice86Creator(
            binName: programPath,
            enablePit: true,
            maxCycles: 300000,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: true,
            enableEms: true,
            cDrive: cDrive
        );
        using Spice86DependencyInjection spice86 = creator.Create();

        spice86.Machine.CpuState.Flags.CpuModel = CpuModel.INTEL_80286;
        spice86.ProgramExecutor.Run();
        return ReadVideoMemory(spice86.Machine.Memory, expectedLength);
    }

    private static string ReadVideoMemory(IMemory memory, int length) {
        uint videoBase = MemoryUtils.ToPhysicalAddress(0xB800, 0);
        StringBuilder output = new(length);
        for (int i = 0; i < length; i++) {
            byte character = memory.UInt8[videoBase + (uint)(i * 2)];
            output.Append((char)character);
        }
        return output.ToString();
    }
}
