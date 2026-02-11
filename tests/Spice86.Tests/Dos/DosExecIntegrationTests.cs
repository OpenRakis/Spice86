namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

using System;
using System.IO;
using System.Text;

using Xunit;

public class DosExecIntegrationTests {
    [Fact]
    public void ExecModesAndOverlays_ShouldReportSuccessViaVideoMemory() {
        // Arrange
        string tempDir = CreateTempDirectory("dos_exec");
        CopyResourceFiles("DosExecTests", tempDir,
            new[] { "dos_exec_master.com", "child.com", "tsr_hook.com", "overlay_driver.bin" });
        File.Move(Path.Join(tempDir, "overlay_driver.bin"), Path.Join(tempDir, "dos_exec_master.000"));

        try {
            // Act
            string output = RunProgramAndReadVideoOutput(
                Path.Join(tempDir, "dos_exec_master.com"), tempDir, expectedLength: 10);

            // Assert
            output.Should().Be("SEMJCTLOAV");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbProcessIsolation_ParentFcbSurvivesChildTermination() {
        // Arrange
        string tempDir = CreateTempDirectory("dos_fcb");
        CopyResourceFiles("DosFcbTests", tempDir,
            new[] { "fcb_process_isolation.com", "fcbchild.com" });

        try {
            // Act
            string output = RunProgramAndReadVideoOutput(
                Path.Join(tempDir, "fcb_process_isolation.com"), tempDir, expectedLength: 7);

            // Assert
            output.Should().Be("SCWOXER");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory(string prefix) {
        string tempDir = Path.Join(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void CopyResourceFiles(string resourceSubDir, string tempDir, string[] files) {
        string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", resourceSubDir);
        foreach (string file in files) {
            File.Copy(Path.Join(resourceDir, file), Path.Join(tempDir, file), overwrite: true);
        }
    }

    private static string RunProgramAndReadVideoOutput(string programPath, string cDrive, int expectedLength) {
        Spice86DependencyInjection spice86 = new Spice86Creator(
            binName: programPath,
            enablePit: true,
            maxCycles: 300000,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: true,
            enableEms: true,
            cDrive: cDrive
        ).Create();

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

    private static void TryDeleteDirectory(string directoryPath) {
        if (!Directory.Exists(directoryPath)) {
            return;
        }
        Directory.Delete(directoryPath, true);
    }
}
