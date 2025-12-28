namespace Spice86.Tests.Dos;

using FluentAssertions;

using System;
using System.IO;

using Spice86.Core.Emulator.CPU;
using Xunit;

public class DosExecRegisterInitializationTests {
    [Fact]
    public void ComLaunch_ShouldInitializeRegistersLikeDos() {
        string resourceDir = Path.Combine(AppContext.BaseDirectory, "Resources", "DosExecIntegration");
        string tempDir = Path.Combine(Path.GetTempPath(), $"dos_exec_regs_com_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        File.Copy(Path.Combine(resourceDir, "child.com"), Path.Combine(tempDir, "child.com"), true);

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: Path.Combine(tempDir, "child.com"),
                enableCfgCpu: false,
                enablePit: false,
                recordData: false,
                maxCycles: 50000,
                installInterruptVectors: true,
                enableA20Gate: false,
                enableXms: true,
                enableEms: true,
                cDrive: tempDir
            ).Create();

            State state = spice86.Machine.CpuState;
            // COM files load at offset 0x100 in their PSP segment
            state.CS.Should().NotBe(0);
            state.IP.Should().Be(0x100);
            // For COM files, all segment registers point to the PSP
            state.DS.Should().Be(state.CS);
            state.ES.Should().Be(state.CS);
            state.SS.Should().Be(state.CS);
            // Stack pointer initialized to end of segment
            state.SP.Should().Be(0xFFFE);

            // All general purpose registers initialized to zero
            state.AX.Should().Be(0);
            state.BX.Should().Be(0);
            state.CX.Should().Be(0);
            state.DX.Should().Be(0);
            state.SI.Should().Be(0);
            state.DI.Should().Be(0);
            state.BP.Should().Be(0);
            state.DirectionFlag.Should().BeFalse();
            state.CarryFlag.Should().BeFalse();
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ExeLaunch_ShouldInitializeRegistersLikeDos() {
        string resourceDir = Path.Combine(AppContext.BaseDirectory, "Resources", "DosExecIntegration");
        string tempDir = Path.Combine(Path.GetTempPath(), $"dos_exec_regs_exe_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        File.Copy(Path.Combine(resourceDir, "overlay_driver.bin"), Path.Combine(tempDir, "overlay_driver.exe"), true);

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: Path.Combine(tempDir, "overlay_driver.exe"),
                enableCfgCpu: false,
                enablePit: false,
                recordData: false,
                maxCycles: 50000,
                installInterruptVectors: true,
                enableA20Gate: false,
                enableXms: true,
                enableEms: true,
                cDrive: tempDir
            ).Create();

            State state = spice86.Machine.CpuState;
            // EXE header specifies InitCS=0, InitIP=0, InitSS=0, InitSP=0xFFFE
            // These are adjusted by loadImageSegment = pspSegment + 0x10
            state.CS.Should().Be(state.SS);
            state.IP.Should().Be(0);
            state.SP.Should().Be(0xFFFE);
            // DS and ES both point to PSP (pspSegment), which is 0x10 less than CS/SS
            state.DS.Should().Be(state.ES);
            state.CS.Should().Be((ushort)(state.DS + 0x10));
            // All general purpose registers initialized to zero
            state.AX.Should().Be(0);
            state.BX.Should().Be(0);
            state.CX.Should().Be(0);
            state.DX.Should().Be(0);
            state.SI.Should().Be(0);
            state.DI.Should().Be(0);
            state.BP.Should().Be(0);
            state.DirectionFlag.Should().BeFalse();
            state.CarryFlag.Should().BeFalse();
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    private static void TryDeleteDirectory(string directoryPath) {
        if (!Directory.Exists(directoryPath)) {
            return;
        }
        Directory.Delete(directoryPath, true);
    }
}
