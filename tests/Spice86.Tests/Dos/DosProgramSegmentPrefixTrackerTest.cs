namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Xunit;

using Configuration = Spice86.Core.CLI.Configuration;
using EmulatorBreakpointsManager = Spice86.Core.Emulator.VM.Breakpoint.EmulatorBreakpointsManager;
using PauseHandler = Spice86.Core.Emulator.VM.PauseHandler;
using State = Spice86.Core.Emulator.CPU.State;

/// <summary>
/// Verifies that the DOS PSP tracker reads the configuration and adds/removes the PSP segments for
/// the loaded/running programs correctly.
/// </summary>
public class DosProgramSegmentPrefixTrackerTests {
    // The instance of the DosProgramSegmentPrefixTracker class that we're testing
    private readonly DosProgramSegmentPrefixTracker _pspTracker;

    /// <summary>
    /// Creates the DosProgramSegmentPrefixTracker instance for each test case.
    /// </summary>
    public DosProgramSegmentPrefixTrackerTests() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();

        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        PauseHandler pauseHandler = new(loggerService);
        State cpuState = new(CpuModel.INTEL_80286);
        EmulatorBreakpointsManager emulatorBreakpointsManager = new(pauseHandler, cpuState);
        A20Gate a20Gate = new(enabled: false);
        Memory memory = new(emulatorBreakpointsManager.MemoryReadWriteBreakpoints, ram, a20Gate,
            initializeResetVector: true);

        var configuration = new Configuration {
            ProgramEntryPointSegment = (ushort)0x1000
        };
        _pspTracker = new(configuration, memory,
            new DosSwappableDataArea(memory, MemoryUtils.ToPhysicalAddress(0xb2, 0)),
            loggerService);
    }

    /// <summary>
    /// Ensures that the initial PSP is calculated from the program entry point segment in the
    /// configuration correctly.
    /// </summary>
    [Fact]
    public void CheckInitialPspSegment() {
        // Assert
        _pspTracker.InitialPspSegment.Should().Be(0xFF0);
        _pspTracker.PspCount.Should().Be(0);
        _pspTracker.GetCurrentPsp().Should().BeNull();
        _pspTracker.GetCurrentPspSegment().Should().Be(0xFF0);
        _pspTracker.GetProgramEntryPointSegment().Should().Be(0x1000);
    }

    /// <summary>
    /// Ensures that new PSP segments can be added.
    /// </summary>
    [Fact]
    public void AddPspSegment() {
        // Act
        DosProgramSegmentPrefix psp1 = _pspTracker.PushPspSegment(0x2000);
        DosProgramSegmentPrefix psp2 = _pspTracker.PushPspSegment(0x3400);
        DosProgramSegmentPrefix psp3 = _pspTracker.PushPspSegment(0x7060);

        // Assert
        psp1.BaseAddress.Should().Be(0x20000);
        psp2.BaseAddress.Should().Be(0x34000);
        psp3.BaseAddress.Should().Be(0x70600);
        _pspTracker.InitialPspSegment.Should().Be(0xFF0);
        _pspTracker.PspCount.Should().Be(3);
        _pspTracker.GetCurrentPsp().Should().Be(psp3);
        _pspTracker.GetCurrentPspSegment().Should().Be(0x7060);
        _pspTracker.GetProgramEntryPointSegment().Should().Be(0x7070);
    }

    /// <summary>
    /// Ensures that PSP segments can be removed.
    /// </summary>
    [Fact]
    public void RemovePspSegment() {
        // Act
        DosProgramSegmentPrefix psp1 = _pspTracker.PushPspSegment(0x2000);
        DosProgramSegmentPrefix psp2 = _pspTracker.PushPspSegment(0x3000);
        DosProgramSegmentPrefix psp3 = _pspTracker.PushPspSegment(0x4000);
        DosProgramSegmentPrefix psp4 = _pspTracker.PushPspSegment(0x5400);
        bool removedInvalidPsp = _pspTracker.PopPspSegment(0x3100);
        bool removedPsp2 = _pspTracker.PopPspSegment(0x3000);
        _pspTracker.PopCurrentPspSegment();

        // Assert
        removedInvalidPsp.Should().BeFalse();
        removedPsp2.Should().BeTrue();
        psp1.BaseAddress.Should().Be(0x20000);
        psp2.BaseAddress.Should().Be(0x30000);
        psp3.BaseAddress.Should().Be(0x40000);
        psp4.BaseAddress.Should().Be(0x54000);
        _pspTracker.InitialPspSegment.Should().Be(0xFF0);
        _pspTracker.PspCount.Should().Be(2);
        _pspTracker.GetCurrentPsp().Should().Be(psp3);
        _pspTracker.GetCurrentPspSegment().Should().Be(0x4000);
        _pspTracker.GetProgramEntryPointSegment().Should().Be(0x4010);
    }

    /// <summary>
    /// Ensures that SetCurrentPspSegment updates the current PSP segment value and GetCurrentPspSegment
    /// returns the newly set value. This verifies the INT 21H/50H and INT 21H/51H consistency.
    /// </summary>
    [Fact]
    public void SetCurrentPspSegmentUpdatesGetCurrentPspSegment() {
        // Arrange
        ushort arbitraryPspSegment = 0x1234;

        // Act
        _pspTracker.SetCurrentPspSegment(arbitraryPspSegment);

        // Assert
        _pspTracker.GetCurrentPspSegment().Should().Be(arbitraryPspSegment);
    }

    /// <summary>
    /// Ensures that SetCurrentPspSegment can set an arbitrary PSP value that doesn't correspond
    /// to a loaded PSP in the internal stack. This is valid DOS behavior where programs can set
    /// the PSP to any segment value.
    /// </summary>
    [Fact]
    public void SetCurrentPspSegmentWorksWithArbitraryValue() {
        // Arrange
        DosProgramSegmentPrefix psp1 = _pspTracker.PushPspSegment(0x2000);
        ushort arbitraryPspSegment = 0x9999; // Not in the PSP stack

        // Act
        _pspTracker.SetCurrentPspSegment(arbitraryPspSegment);

        // Assert
        _pspTracker.GetCurrentPspSegment().Should().Be(arbitraryPspSegment);
        // The internal PSP stack should remain unchanged
        _pspTracker.PspCount.Should().Be(1);
        _pspTracker.GetCurrentPsp().Should().Be(psp1);
    }

    /// <summary>
    /// Ensures that pushing a new PSP segment updates GetCurrentPspSegment to the new segment.
    /// This verifies that PushPspSegment keeps the SDA in sync with the stack.
    /// </summary>
    [Fact]
    public void PushPspSegmentUpdatesGetCurrentPspSegment() {
        // Act
        _pspTracker.PushPspSegment(0x2000);

        // Assert
        _pspTracker.GetCurrentPspSegment().Should().Be(0x2000);
    }

    /// <summary>
    /// Ensures that popping a PSP segment updates GetCurrentPspSegment to reflect the new top of stack.
    /// This verifies that PopPspSegment keeps the SDA in sync with the stack.
    /// </summary>
    [Fact]
    public void PopPspSegmentUpdatesGetCurrentPspSegment() {
        // Arrange
        _pspTracker.PushPspSegment(0x2000);
        _pspTracker.PushPspSegment(0x3000);
        _pspTracker.GetCurrentPspSegment().Should().Be(0x3000);

        // Act
        _pspTracker.PopCurrentPspSegment();

        // Assert
        _pspTracker.GetCurrentPspSegment().Should().Be(0x2000);
    }

    /// <summary>
    /// Ensures that after setting a custom PSP via SetCurrentPspSegment, pushing a new PSP
    /// will update the current PSP to the newly pushed segment.
    /// </summary>
    [Fact]
    public void PushPspSegmentOverridesManuallySetPsp() {
        // Arrange
        _pspTracker.SetCurrentPspSegment(0x9999);
        _pspTracker.GetCurrentPspSegment().Should().Be(0x9999);

        // Act
        _pspTracker.PushPspSegment(0x4000);

        // Assert
        _pspTracker.GetCurrentPspSegment().Should().Be(0x4000);
    }
}