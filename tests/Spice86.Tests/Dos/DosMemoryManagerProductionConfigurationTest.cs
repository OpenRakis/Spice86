namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Xunit;

using Configuration = Spice86.Core.CLI.Configuration;

/// <summary>
/// Verifies that the DOS memory manager works correctly with production configuration.
/// This test uses the default ProgramEntryPointSegment=0x170 to verify that the fix
/// for maximizing conventional memory works in real-world scenarios.
/// </summary>
public class DosMemoryManagerProductionConfigurationTest {
    private readonly ILoggerService _loggerService;
    private readonly IMemory _memory;
    private readonly DosProgramSegmentPrefixTracker _pspTracker;
    private readonly DosMemoryManager _memoryManager;

    public DosMemoryManagerProductionConfigurationTest() {
        _loggerService = Substitute.For<ILoggerService>();

        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        A20Gate a20Gate = new(enabled: false);
        _memory = new Memory(memoryBreakpoints, ram, a20Gate, initializeResetVector: true);

        // Use production configuration with default ProgramEntryPointSegment
        Configuration configuration = new Configuration {
            ProgramEntryPointSegment = 0x170  // Default production value
        };
        DosSwappableDataArea dosSwappableDataArea = new(_memory, MemoryUtils.ToPhysicalAddress(DosSwappableDataArea.BaseSegment, 0));
        _pspTracker = new(configuration, _memory, dosSwappableDataArea, _loggerService);

        _memoryManager = new DosMemoryManager(_memory, _pspTracker, _loggerService);
    }

    /// <summary>
    /// Verifies that with production configuration following FreeDOS/DOSBox pattern,
    /// the memory manager provides approximately 631 KB of conventional memory.
    /// This follows the FreeDOS kernel and DOSBox approach where the MCB chain
    /// starts at 0x016F with device/system blocks before user memory.
    /// </summary>
    [Fact]
    public void ProvideMaximumConventionalMemory() {
        // Act
        DosMemoryControlBlock largestFree = _memoryManager.FindLargestFree();

        // Assert
        largestFree.Should().NotBeNull();
        largestFree.IsValid.Should().BeTrue();
        largestFree.IsFree.Should().BeTrue();
        largestFree.IsLast.Should().BeTrue();
        
        // Following FreeDOS/DOSBox pattern:
        // - Device MCB at 0x016F (size 1)
        // - Env MCB at 0x0171 (size 4) 
        // - Locked MCB at 0x0176 (size 16)
        // - Free MCB at 0x0187, data starts at 0x0188
        largestFree.DataBlockSegment.Should().Be(0x0188);
        
        // Size = (0x9FFF - 0x0187) = 0x9E78 paragraphs = 40568 paragraphs
        largestFree.Size.Should().Be(0x9E78);
        
        // Total bytes = 40568 * 16 = 649,088 bytes (~634 KB)
        int expectedBytes = 0x9E78 * 16;
        largestFree.AllocationSizeInBytes.Should().Be(expectedBytes);
        
        // Verify this is more than 630 KB
        largestFree.AllocationSizeInBytes.Should().BeGreaterThan(630 * 1024);
    }

    /// <summary>
    /// Verifies that large allocations (like those attempted by Day of the Tentacle)
    /// can be successfully made from the available conventional memory.
    /// </summary>
    [Fact]
    public void AllocateLargeMemoryBlocks() {
        // Day of the Tentacle tries to allocate large blocks (512 KB, 448 KB, etc.)
        // With increased conventional memory, these should succeed if there's enough space
        
        // Act - Try to allocate a large block (400 KB = 25000 paragraphs)
        DosMemoryControlBlock? block = _memoryManager.AllocateMemoryBlock(25000);

        // Assert
        block.Should().NotBeNull();
        block!.IsValid.Should().BeTrue();
        block!.IsFree.Should().BeFalse();
        block!.Size.Should().Be(25000);
        block!.AllocationSizeInBytes.Should().Be(25000 * 16);  // 400,000 bytes
    }

    /// <summary>
    /// Verifies the device/system MCB at start of chain (FreeDOS/DOSBox pattern).
    /// </summary>
    [Fact]
    public void DeviceMcbIsAtChainStart() {
        // Following FreeDOS/DOSBox: First MCB at 0x016F is device/system block (PSP=0x0008)
        DosMemoryControlBlock deviceMcb = new DosMemoryControlBlock(
            _memory, 
            MemoryUtils.ToPhysicalAddress(0x016F, 0)
        );

        // Assert
        deviceMcb.IsValid.Should().BeTrue();
        deviceMcb.IsFree.Should().BeFalse();  // Allocated to system
        deviceMcb.IsLast.Should().BeFalse();   // Not the last block
        deviceMcb.DataBlockSegment.Should().Be(0x0170);
        deviceMcb.Size.Should().Be(1);  // 1 paragraph for device block
        deviceMcb.PspSegment.Should().Be(0x0008);  // MCB_DOS - system owner
    }
}
