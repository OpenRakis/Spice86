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
    /// Verifies that with production configuration, the memory manager provides
    /// approximately 639 KB of conventional memory, which is the maximum possible
    /// after accounting for COMMAND.COM and DOS structures.
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
        
        // The free memory should start at 0x61 (after COMMAND.COM and its MCB)
        // COMMAND.COM is at 0x50, its MCB at 0x4F, free MCB at 0x60, data at 0x61
        largestFree.DataBlockSegment.Should().Be(0x61);
        
        // Size should be approximately (0x9FFF - 0x60) = 0x9F9F paragraphs = 40863 paragraphs
        largestFree.Size.Should().Be(0x9F9F);
        
        // Total bytes should be approximately 654,008 bytes (~639 KB)
        int expectedBytes = 0x9F9F * 16;
        largestFree.AllocationSizeInBytes.Should().Be(expectedBytes);
        
        // Verify this is more than 638 KB (better than the old ~634 KB)
        largestFree.AllocationSizeInBytes.Should().BeGreaterThan(638 * 1024);
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
    /// Verifies that COMMAND.COM is properly integrated into the MCB chain.
    /// </summary>
    [Fact]
    public void CommandComIsInMcbChain() {
        // COMMAND.COM should be at segment 0x50 with its MCB at 0x4F
        DosMemoryControlBlock commandComMcb = new DosMemoryControlBlock(
            _memory, 
            MemoryUtils.ToPhysicalAddress(CommandCom.CommandComSegment - 1, 0)
        );

        // Assert
        commandComMcb.IsValid.Should().BeTrue();
        commandComMcb.IsFree.Should().BeFalse();  // Should be allocated
        commandComMcb.IsLast.Should().BeFalse();   // Not the last block
        commandComMcb.DataBlockSegment.Should().Be(CommandCom.CommandComSegment);
        commandComMcb.Size.Should().Be(0x10);  // 16 paragraphs for PSP
        commandComMcb.PspSegment.Should().Be(CommandCom.CommandComSegment);  // Owned by itself
    }
}
