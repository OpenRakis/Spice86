namespace Spice86.Tests.Emulator.Devices.DirectMemoryAccess;

using FluentAssertions;

using NSubstitute;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using Xunit;

public class DmaBusTests {
    [Fact]
    public void Address_register_writes_update_channel_state() {
        DmaBus system = CreateSystem(out _, out _, out _);
        // Clear flip-flop to ensure deterministic writes
        system.WriteByte(0x0C, 0x00);

        system.WriteByte(0x00, 0x34);
        system.WriteByte(0x00, 0x12);
        system.WriteByte(0x01, 0x03);
        system.WriteByte(0x01, 0x00);

        DmaChannel? channel = system.GetChannel(0);
        channel.Should().NotBeNull();
        channel!.BaseAddress.Should().Be(0x1234);
        channel.CurrentAddress.Should().Be(0x1234);
        channel.BaseCount.Should().Be(0x0003);
        channel.CurrentCount.Should().Be(0x0003);
    }

    [Fact]
    public void Mask_register_masks_and_unmasks_channel() {
        DmaBus system = CreateSystem(out _, out _, out _);
        DmaChannel channel = system.GetChannel(0)!;

        // Mask channel 0
        system.WriteByte(0x0A, 0x04);
        channel.IsMasked.Should().BeTrue();

        // Unmask channel 0
        system.WriteByte(0x0A, 0x00);
        channel.IsMasked.Should().BeFalse();
    }

    [Fact]
    public void Page_registers_set_page_base() {
        DmaBus system = CreateSystem(out _, out _, out _);
        DmaChannel channel = system.GetChannel(0)!;

        system.WriteByte(0x87, 0x7F);
        channel.PageRegisterValue.Should().Be(0x7F);
        channel.PageBase.Should().Be((uint)0x7F << 16);
    }

    [Fact]
    public void Channel_read_uses_memory_contents() {
        DmaBus system = CreateSystem(out Memory memory, out _, out _);
        DmaChannel channel = system.GetChannel(0)!;

        // Program address, count, and page registers for channel 0
        system.WriteByte(0x0C, 0x00);
        system.WriteByte(0x00, 0x00);
        system.WriteByte(0x00, 0x00);
        system.WriteByte(0x01, 0x03);
        system.WriteByte(0x01, 0x00);
        system.WriteByte(0x87, 0x00);

        memory[0] = 0xAA;
        memory[1] = 0xBB;
        memory[2] = 0xCC;
        memory[3] = 0xDD;

        Span<byte> buffer = stackalloc byte[4];
        int words = channel.Read(4, buffer);

        words.Should().Be(4);
        buffer.ToArray().Should().Equal(0xAA, 0xBB, 0xCC, 0xDD);
        channel.CurrentAddress.Should().Be(4);
    }

    private static DmaBus CreateSystem(out Memory memory, out State state, out IOPortDispatcher dispatcher) {
        state = new State(CpuModel.INTEL_8086);
        ILoggerService logger = Substitute.For<ILoggerService>();
        logger.IsEnabled(Arg.Any<LogEventLevel>()).Returns(false);

        AddressReadWriteBreakpoints ioBreakpoints = new();
        dispatcher = new IOPortDispatcher(ioBreakpoints, state, logger, false);

        AddressReadWriteBreakpoints memoryBreakpoints = new();
        memory = new Memory(memoryBreakpoints, new Ram(0x200000), new A20Gate());

        return new DmaBus(memory, state, dispatcher, false, logger);
    }
}