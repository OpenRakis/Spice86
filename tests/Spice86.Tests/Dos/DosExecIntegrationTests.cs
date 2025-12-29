namespace Spice86.Tests.Dos;

using FluentAssertions;

using System;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

using System.IO;
using System.Text;

using Xunit;

public class DosExecIntegrationTests {
    [Fact]
    public void ExecModesAndOverlays_ShouldReportSuccessViaVideoMemory() {
        string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", "DosExecIntegration");
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_exec_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        foreach (string file in new[] { "dos_exec_master.com", "child.com", "tsr_hook.com", "overlay_driver.bin" }) {
            string source = Path.Join(resourceDir, file);
            string targetName = file == "overlay_driver.bin" ? "dos_exec_master.000" : file;
            File.Copy(source, Path.Join(tempDir, targetName), overwrite: true);
        }

        string programPath = Path.Join(tempDir, "dos_exec_master.com");

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: programPath,
                enableCfgCpu: false,
                enablePit: true,
                recordData: false,
                maxCycles: 300000,
                installInterruptVectors: true,
                enableA20Gate: false,
                enableXms: true,
                enableEms: true,
                cDrive: tempDir
            ).Create();

            spice86.Machine.CpuState.Flags.CpuModel = CpuModel.INTEL_80286;

            spice86.ProgramExecutor.Run();

            IMemory memory = spice86.Machine.Memory;
            uint videoBase = MemoryUtils.ToPhysicalAddress(0xB800, 0);
            const int expectedLength = 10;
            StringBuilder output = new(expectedLength);

            for (int i = 0; i < expectedLength; i++) {
                byte character = memory.UInt8[videoBase + (uint)(i * 2)];
                output.Append((char)character);
            }

            output.ToString().Should().Be("SEMJCTLOAV");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ExecLoadOnly_FromEnvName_ShouldLoadOverlayAndResume() {
        string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", "DosExecIntegration");
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_exec_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        foreach (string file in new[] { "tentacle.bin", "tentacle.000" }) {
            string source = Path.Join(resourceDir, file);
            string target = file == "tentacle.bin" ? "tentacle.exe" : file;
            File.Copy(source, Path.Join(tempDir, target), overwrite: true);
        }

        string programPath = Path.Join(tempDir, "tentacle.exe");

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: programPath,
                enableCfgCpu: false,
                enablePit: true,
                recordData: false,
                maxCycles: 200000,
                installInterruptVectors: true,
                enableA20Gate: false,
                enableXms: true,
                enableEms: true,
                cDrive: tempDir
            ).Create();

            spice86.ProgramExecutor.Run();

            IMemory memory = spice86.Machine.Memory;
            uint videoBase = MemoryUtils.ToPhysicalAddress(0xB800, 0);
            byte character = memory.UInt8[videoBase];
            ((char)character).Should().Be('K');
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ExecLoadOnly_FromSameImage_ShouldResumeAfterLoad() {
        string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", "DosExecIntegration");
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_exec_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string source = Path.Join(resourceDir, "selfload.bin");
        string target = Path.Join(tempDir, "selfload.exe");
        File.Copy(source, target, overwrite: true);

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: target,
                enableCfgCpu: false,
                enablePit: true,
                recordData: false,
                maxCycles: 200000,
                installInterruptVectors: true,
                enableA20Gate: false,
                enableXms: true,
                enableEms: true,
                cDrive: tempDir
            ).Create();

            spice86.ProgramExecutor.Run();

            IMemory memory = spice86.Machine.Memory;
            uint videoBase = MemoryUtils.ToPhysicalAddress(0xB800, 0);
            byte character = memory.UInt8[videoBase];
            ((char)character).Should().Be('O');
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    private static void TryDeleteDirectory(string directoryPath) {
        if (!Directory.Exists(directoryPath)) {
            return;
        }

        try {
            Directory.Delete(directoryPath, true);
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
    }
}
