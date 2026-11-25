namespace Spice86.Tests.Emulator.Devices.DirectMemoryAccess;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using System.Text.Json;

using Xunit;

/// <summary>
/// Checks <see cref="DmaBus.CaptureState" /> and <see cref="DmaBus.RestoreState" />: the controllers are
/// programmed and read through their ports, and transfers run through <see cref="DmaChannel.Read" />.
/// </summary>
public sealed class DmaBusStateTests {
    private const ushort Channel1AddressPort = 0x02;
    private const ushort Channel1CountPort = 0x03;
    private const ushort Channel3AddressPort = 0x06;
    private const ushort PrimaryStatusPort = 0x08;
    private const ushort PrimaryMaskPort = 0x0A;
    private const ushort PrimaryModePort = 0x0B;
    private const ushort PrimaryFlipFlopResetPort = 0x0C;
    private const ushort PrimaryMasterClearPort = 0x0D;
    private const ushort Channel1PagePort = 0x83;
    private const ushort Channel5AddressPort = 0xC4;
    private const ushort SecondaryFlipFlopResetPort = 0xD8;
    private const byte Channel1AutoInitRead = 0x59;
    private const byte UnmaskChannel1 = 0x01;
    private const byte MaskBit = 0x04;
    private const byte Channel1TerminalCountBit = 0x02;

    private readonly IOPortDispatcher _ioPortDispatcher;
    private readonly Memory _memory;
    private readonly DmaBus _dmaBus;

    public DmaBusStateTests() {
        State state = new(CpuModel.INTEL_80286);
        ILogger logger = Substitute.For<ILogger>();
        _ioPortDispatcher = new IOPortDispatcher(new AddressReadWriteBreakpoints(), state, logger, false);
        _memory = new Memory(new AddressReadWriteBreakpoints(), new Ram(0x200000), new A20Gate(),
            new RealModeMmu386(), false);
        _dmaBus = new DmaBus(_memory, state, _ioPortDispatcher, false, logger);
    }

    [Fact]
    public void CaptureChangesNothing() {
        // Arrange
        ProgramChannel1(page: 0x05, address: 0x1234, count: 0x0003);

        // Act
        DmaBusState first = _dmaBus.CaptureState();
        DmaBusState second = _dmaBus.CaptureState();

        // Assert
        second.Should().Be(first);
        ReadChannel1Address().Should().Be(0x1234);
        ReadChannel1Count().Should().Be(0x0003);
    }

    [Fact]
    public void RestoreAfterMasterClearAndReprogrammingGivesTheCapturedPortReads() {
        // Arrange: channel 1 programmed, a half-written address on channel 3 and on channel 5
        ProgramChannel1(page: 0x05, address: 0x1234, count: 0x0003);
        _ioPortDispatcher.WriteByte(Channel3AddressPort, 0x78);
        _ioPortDispatcher.WriteByte(SecondaryFlipFlopResetPort, 0);
        _ioPortDispatcher.WriteByte(Channel5AddressPort, 0x9A);
        DmaBusState captured = _dmaBus.CaptureState();

        // Act
        _ioPortDispatcher.WriteByte(PrimaryMasterClearPort, 0);
        ProgramChannel1(page: 0x0A, address: 0x5678, count: 0x0100);
        _ioPortDispatcher.WriteByte(SecondaryFlipFlopResetPort, 0);
        _dmaBus.RestoreState(captured);

        // Assert: the flip-flops still point at the high bytes, so the next address writes complete the
        // half-written addresses
        _dmaBus.CaptureState().Should().Be(captured);
        _ioPortDispatcher.WriteByte(Channel3AddressPort, 0x56);
        _ioPortDispatcher.WriteByte(Channel5AddressPort, 0xBC);
        Channel(3).CurrentAddress.Should().Be(0x5678);
        Channel(5).CurrentAddress.Should().Be(0xBC9A);
        _ioPortDispatcher.ReadByte(Channel1PagePort).Should().Be(0x05);
        ReadChannel1Address().Should().Be(0x1234);
        ReadChannel1Count().Should().Be(0x0003);
        Channel(1).IsMasked.Should().BeFalse();
    }

    [Fact]
    public void TransferAfterEachRestoreReadsTheCapturedAddressAndReportsTerminalCount() {
        // Arrange
        WriteMemory(0x51234, 0xAA, 0xBB, 0xCC, 0xDD);
        ProgramChannel1(page: 0x05, address: 0x1234, count: 0x0003);
        DmaBusState captured = _dmaBus.CaptureState();
        byte[] expected = [0xAA, 0xBB, 0xCC, 0xDD];

        // Act: run the transfer, restore, run it again; restore once more and run it a third time
        byte[] firstRead = ReadFourBytesFromChannel1();
        _dmaBus.RestoreState(captured);
        byte statusAfterFirstRestore = _ioPortDispatcher.ReadByte(PrimaryStatusPort);
        byte[] secondRead = ReadFourBytesFromChannel1();
        byte statusAfterSecondRead = _ioPortDispatcher.ReadByte(PrimaryStatusPort);
        _dmaBus.RestoreState(captured);
        byte[] thirdRead = ReadFourBytesFromChannel1();

        // Assert
        firstRead.Should().Equal(expected);
        secondRead.Should().Equal(expected);
        thirdRead.Should().Equal(expected);
        (statusAfterFirstRestore & Channel1TerminalCountBit).Should().Be(0);
        (statusAfterSecondRead & Channel1TerminalCountBit).Should().Be(Channel1TerminalCountBit);
    }

    [Fact]
    public void RestoreDoesNotCallTheChannelCallback() {
        // Arrange
        ProgramChannel1(page: 0x05, address: 0x1234, count: 0x0003);
        DmaBusState captured = _dmaBus.CaptureState();
        _ioPortDispatcher.WriteByte(PrimaryMaskPort, MaskBit | UnmaskChannel1);
        List<DmaChannel.DmaEvent> events = new();
        Channel(1).RegisterCallback((_, dmaEvent) => events.Add(dmaEvent));
        events.Clear();

        // Act
        _dmaBus.RestoreState(captured);

        // Assert
        events.Should().BeEmpty();
        Channel(1).IsMasked.Should().BeFalse();
    }

    [Fact]
    public void StateReadBackFromJsonRestoresTheCapturedState() {
        // Arrange
        ProgramChannel1(page: 0x05, address: 0x1234, count: 0x0003);
        DmaBusState captured = _dmaBus.CaptureState();
        string json = JsonSerializer.Serialize(captured);

        // Act
        _ioPortDispatcher.WriteByte(PrimaryMasterClearPort, 0);
        DmaBusState deserialized = JsonSerializer.Deserialize<DmaBusState>(json);
        _dmaBus.RestoreState(deserialized);

        // Assert
        deserialized.Should().Be(captured);
        _dmaBus.CaptureState().Should().Be(captured);
        ReadChannel1Address().Should().Be(0x1234);
    }

    private DmaChannel Channel(byte channelNumber) {
        return _dmaBus.GetChannel(channelNumber)
            ?? throw new InvalidOperationException($"No DMA channel {channelNumber}.");
    }

    private void ProgramChannel1(byte page, ushort address, ushort count) {
        _ioPortDispatcher.WriteByte(PrimaryFlipFlopResetPort, 0);
        _ioPortDispatcher.WriteByte(PrimaryModePort, Channel1AutoInitRead);
        _ioPortDispatcher.WriteByte(Channel1PagePort, page);
        _ioPortDispatcher.WriteByte(Channel1AddressPort, (byte)address);
        _ioPortDispatcher.WriteByte(Channel1AddressPort, (byte)(address >> 8));
        _ioPortDispatcher.WriteByte(Channel1CountPort, (byte)count);
        _ioPortDispatcher.WriteByte(Channel1CountPort, (byte)(count >> 8));
        _ioPortDispatcher.WriteByte(PrimaryMaskPort, UnmaskChannel1);
    }

    private ushort ReadChannel1Address() {
        byte low = _ioPortDispatcher.ReadByte(Channel1AddressPort);
        byte high = _ioPortDispatcher.ReadByte(Channel1AddressPort);
        return (ushort)(low | (high << 8));
    }

    private ushort ReadChannel1Count() {
        byte low = _ioPortDispatcher.ReadByte(Channel1CountPort);
        byte high = _ioPortDispatcher.ReadByte(Channel1CountPort);
        return (ushort)(low | (high << 8));
    }

    private byte[] ReadFourBytesFromChannel1() {
        byte[] buffer = new byte[4];
        Channel(1).Read(4, buffer);
        return buffer;
    }

    private void WriteMemory(uint address, params byte[] values) {
        for (int i = 0; i < values.Length; i++) {
            _memory[(uint)(address + i)] = values[i];
        }
    }
}
