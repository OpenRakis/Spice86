namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

using Xunit;

public class HltTest {
    private const ushort HandlerOffset = 0x002A;
    private const ushort AfterFirstHltOffset = 0x0028;
    private const ushort AfterSecondHltOffset = 0x002A;

    /// <summary>
    /// BIOS image: hooks IRQ0, programs the PIT for about 1 ms, then runs STI, HLT, CLI, HLT.
    /// The IRQ0 handler sets DX to 1.
    /// </summary>
    private static readonly byte[] StiHltProgram = [
        0xFA,                               // 0000 cli
        0x31, 0xC0,                         // 0001 xor ax,ax
        0x8E, 0xD0,                         // 0003 mov ss,ax
        0xBC, 0x00, 0x80,                   // 0005 mov sp,0x8000
        0x8E, 0xD8,                         // 0008 mov ds,ax
        0xC7, 0x06, 0x20, 0x00, (byte)HandlerOffset, 0x00, // 000A mov word [0x20],handler
        0x8C, 0x0E, 0x22, 0x00,             // 0010 mov [0x22],cs
        0xB0, 0x36, 0xE6, 0x43,             // 0014 mov al,0x36 / out 0x43,al
        0xB0, 0x51, 0xE6, 0x40,             // 0018 mov al,0x51 / out 0x40,al
        0xB0, 0x22, 0xE6, 0x40,             // 001C mov al,0x22 / out 0x40,al
        0xB0, 0x00, 0xE6, 0x21,             // 0020 mov al,0 / out 0x21,al
        0x31, 0xD2,                         // 0024 xor dx,dx
        0xFB,                               // 0026 sti
        0xF4,                               // 0027 hlt
        0xFA,                               // 0028 cli
        0xF4,                               // 0029 hlt
        0xBA, 0x01, 0x00,                   // 002A handler: mov dx,1
        0xB0, 0x20, 0xE6, 0x20,             // 002D mov al,0x20 / out 0x20,al
        0xCF                                // 0031 iret
    ];

    private const long HaltedCycleBreakpoint = 1000;

    /// <summary>
    /// BIOS image: masks every PIC line, then runs STI, HLT. No interrupt can wake the CPU.
    /// </summary>
    private static readonly byte[] MaskedIrqHltProgram = [
        0xFA,                               // 0000 cli
        0xB0, 0xFF, 0xE6, 0x21,             // 0001 mov al,0xFF / out 0x21,al
        0xFB,                               // 0005 sti
        0xF4                                // 0006 hlt
    ];

    [Fact]
    public void HltWithInterruptsEnabledWaitsForHardwareInterrupt() {
        // Arrange
        string binPath = WriteBiosImage();
        try {
            using Spice86Creator creator = new(binPath, enablePit: true) { HltStopsEmulation = false };
            using Spice86DependencyInjection spice86 = creator.Create();

            // Act
            spice86.ProgramExecutor.Run();

            // Assert
            State state = spice86.Machine.CpuState;
            state.DX.Should().Be(1);
            state.IP.Should().Be(AfterSecondHltOffset);
            state.IsHalted.Should().BeFalse();
        } finally {
            File.Delete(binPath);
        }
    }

    [Fact]
    public void HltStopsEmulationOptionEndsAtFirstHlt() {
        // Arrange
        string binPath = WriteBiosImage();
        try {
            using Spice86Creator creator = new(binPath, enablePit: true);
            using Spice86DependencyInjection spice86 = creator.Create();

            // Act
            spice86.ProgramExecutor.Run();

            // Assert
            State state = spice86.Machine.CpuState;
            state.DX.Should().Be(0);
            state.IP.Should().Be(AfterFirstHltOffset);
        } finally {
            File.Delete(binPath);
        }
    }

    [Fact]
    public void CycleBreakpointTriggersWhileHalted() {
        // Arrange
        string binPath = WriteBiosImage(MaskedIrqHltProgram);
        try {
            using Spice86Creator creator = new(binPath, enablePit: true) { HltStopsEmulation = false };
            using Spice86DependencyInjection spice86 = creator.Create();
            State state = spice86.Machine.CpuState;
            bool triggered = false;
            spice86.Machine.EmulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_CYCLES,
                HaltedCycleBreakpoint, (_, _) => {
                    triggered = true;
                    state.IsRunning = false;
                }, true), true);

            // Act
            spice86.ProgramExecutor.Run();

            // Assert
            triggered.Should().BeTrue();
            state.IsHalted.Should().BeTrue();
        } finally {
            File.Delete(binPath);
        }
    }

    private static string WriteBiosImage() {
        return WriteBiosImage(StiHltProgram);
    }

    private static string WriteBiosImage(byte[] program) {
        byte[] image = new byte[0x10000];
        program.CopyTo(image, 0);
        byte[] resetJump = [0xEA, 0x00, 0x00, 0x00, 0xF0];
        resetJump.CopyTo(image, 0xFFF0);
        string path = Path.Join(Path.GetTempPath(), $"hlt-{Guid.NewGuid()}.bin");
        File.WriteAllBytes(path, image);
        return path;
    }
}
