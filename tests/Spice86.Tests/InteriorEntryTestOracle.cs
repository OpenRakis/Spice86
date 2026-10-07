namespace Spice86.Tests;

using FluentAssertions;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;

/// <summary>Shared completion and stack-safety assertions for the interior-entry IRQ fixture.</summary>
internal static class InteriorEntryTestOracle {
    internal const string BinName = "interior_entry";
    internal const long MaxCycles = 100000;
    private const uint HookCountAddress = 0x0400;
    private const uint TailCountAddress = 0x0401;
    private const uint CompletedAddress = 0x0402;
    private const uint ObserverCountAddress = 0x040C;
    private const uint BadStackAddress = 0x040D;
    private const uint ObservedSsAddress = 0x040E;
    private const uint ObservedSpAddress = 0x0410;

    /// <summary>Requires all three phases to finish without an IRQ observing an incomplete stack switch.</summary>
    internal static void AssertCompleted(IMemory memory, State state) {
        memory.UInt8[HookCountAddress].Should().Be(2);
        memory.UInt8[TailCountAddress].Should().Be(3);
        memory.UInt8[ObserverCountAddress].Should().Be(1);
        memory.UInt8[CompletedAddress].Should().Be(0xA5);
        state.SS.Should().Be(0);
        state.SP.Should().Be(0x8000);
        memory.UInt8[BadStackAddress].Should().Be(0,
            $"the IRQ must see a complete stack; observed SS:SP was {memory.UInt16[ObservedSsAddress]:X4}:{memory.UInt16[ObservedSpAddress]:X4}");
    }
}