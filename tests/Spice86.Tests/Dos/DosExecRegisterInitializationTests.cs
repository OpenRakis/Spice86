namespace Spice86.Tests.Dos;

using FluentAssertions;

using System;
using System.IO;

using Spice86.Core.Emulator.CPU;
using Xunit;

public class DosExecRegisterInitializationTests {
    [Fact]
    public void ComLaunch_ShouldInitializeRegistersLikeDos() {
        string resourceDir = Path.GetFullPath(Path.Join("Resources", "DosExecIntegration"));
        string tempDir = Path.Combine(Path.GetTempPath(), $"dos_exec_regs_com_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        File.Copy(Path.Join(resourceDir, "child.com"), Path.Join(tempDir, "child.com"));

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: @"C:\child.com",
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
            state.CS.Should().NotBe(0);
            state.IP.Should().Be(0x100);
            state.DS.Should().Be(state.CS);
            state.ES.Should().Be(state.CS);
            state.SS.Should().Be(state.CS);
            state.SP.Should().Be(0xFFFE);

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
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void ExeLaunch_ShouldInitializeRegistersLikeDos() {
        string resourceDir = Path.GetFullPath(Path.Join("Resources", "DosExecIntegration"));
        string tempDir = Path.Combine(Path.GetTempPath(), $"dos_exec_regs_exe_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        File.Copy(Path.Join(resourceDir, "overlay_driver.exe"), Path.Join(tempDir, "overlay_driver.exe"));

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: @"C:\overlay_driver.exe",
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
            state.CS.Should().NotBe(0);
            state.IP.Should().NotBe(0);
            state.DS.Should().Be(state.ES);
            state.SS.Should().NotBe(0);
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
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
