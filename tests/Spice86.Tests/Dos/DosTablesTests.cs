namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using Xunit;

public class DosTablesTests {
    [Fact]
    public void JobFileTableSegment_IsReusedUntilProcessReleaseAndAfterward() {
        Memory memory = new(new(), new Ram(A20Gate.EndOfHighMemoryArea), new A20Gate(false),
            new RealModeMmu386(), false);
        DosTables dosTables = new(memory);
        ushort ownerPspSegment = 0x1000;
        ushort firstSegment = dosTables.GetJobFileTableSegment(ownerPspSegment);

        dosTables.GetJobFileTableSegment(ownerPspSegment).Should().Be(firstSegment);

        dosTables.ReleaseJobFileTable(ownerPspSegment);

        dosTables.GetJobFileTableSegment(0x2000).Should().Be(firstSegment);
    }
}
