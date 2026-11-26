namespace Spice86.Tests.Dos.Xms;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System.Text.Json;

using Xunit;

/// <summary>
/// Checks <see cref="ExtendedMemoryManager.CaptureState" /> and <see cref="ExtendedMemoryManager.RestoreState" />:
/// capture, change the driver through its XMS functions, restore, and check that the functions answer as they
/// did at capture.
/// </summary>
public sealed class XmsStateTests {
    private const byte AllocateExtendedMemoryBlock = 0x09;
    private const byte FreeExtendedMemoryBlock = 0x0A;
    private const byte MoveExtendedMemoryBlock = 0x0B;
    private const byte LockExtendedMemoryBlock = 0x0C;
    private const byte UnlockExtendedMemoryBlock = 0x0D;
    private const byte GetHandleInformation = 0x0E;
    private const byte RequestHighMemoryArea = 0x01;
    private const byte ReleaseHighMemoryArea = 0x02;
    private const byte LocalEnableA20 = 0x05;
    private const byte LocalDisableA20 = 0x06;
    private const byte HighMemoryAreaInUse = 0x91;
    private const byte BlockIsLocked = 0xAB;
    private const uint MoveStructureAddress = 0x1000;
    private const ushort ConventionalBufferOffset = 0x4000;

    private readonly State _state;
    private readonly Memory _memory;
    private readonly ExtendedMemoryManager _xms;

    public XmsStateTests() {
        _state = new State(CpuModel.INTEL_80286);
        A20Gate a20Gate = new(false);
        _memory = new Memory(new(), new Ram(A20Gate.EndOfHighMemoryArea), a20Gate, new RealModeMmu386(), false);
        ILogger logger = Substitute.For<ILogger>();
        CallbackHandler callbackHandler = new(_state, logger);
        DosTables dosTables = new(_memory);
        MemoryAsmWriter asmWriter = new(_memory, new(0, 0), callbackHandler);
        DosSysVars dosSysVars = DosTestHelpers.CreateDosSysVars(_memory, logger, 0x1000);
        DosMemoryManager dosMemoryManager = new(_memory, 0x1000, logger, dosSysVars, false, false);
        _xms = new ExtendedMemoryManager(_memory, _state, a20Gate, asmWriter, dosTables, dosMemoryManager, logger);
    }

    [Fact]
    public void AllocationAfterEachRestoreReturnsTheHandleItReturnedAfterCapture() {
        // Arrange
        XmsState captured = _xms.CaptureState();
        ushort firstHandle = Allocate();

        // Act
        _xms.RestoreState(captured);
        ushort afterFirstRestore = Allocate();
        _xms.RestoreState(captured);
        ushort afterSecondRestore = Allocate();

        // Assert
        afterFirstRestore.Should().Be(firstHandle);
        afterSecondRestore.Should().Be(firstHandle);
    }

    [Fact]
    public void RestoreBringsBackTheBlockContentAndLockCount() {
        // Arrange
        ushort handle = Allocate();
        WriteToBlock(handle, 0x42);
        Call(LockExtendedMemoryBlock, handle);
        XmsState captured = _xms.CaptureState();

        // Act
        WriteToBlock(handle, 0x99);
        Call(UnlockExtendedMemoryBlock, handle);
        Call(FreeExtendedMemoryBlock, handle).Ax.Should().Be(1);
        _xms.RestoreState(captured);

        // Assert
        ReadFromBlock(handle).Should().Be(0x42);
        Call(GetHandleInformation, handle);
        _state.BH.Should().Be(1, "the block was locked once at capture");
        Call(FreeExtendedMemoryBlock, handle).Bl.Should().Be(BlockIsLocked);
    }

    [Fact]
    public void RestoreBringsBackTheHighMemoryAreaClaimAndTheLocalA20Count() {
        // Arrange
        Call(RequestHighMemoryArea, 0xFFFF).Ax.Should().Be(1);
        Call(LocalEnableA20, 0).Ax.Should().Be(1);
        XmsState captured = _xms.CaptureState();

        // Act
        Call(ReleaseHighMemoryArea, 0).Ax.Should().Be(1);
        Call(LocalDisableA20, 0).Ax.Should().Be(1);
        _xms.RestoreState(captured);

        // Assert
        Call(RequestHighMemoryArea, 0xFFFF).Bl.Should().Be(HighMemoryAreaInUse);
        Call(LocalDisableA20, 0).Ax.Should().Be(1, "one local enable was pending at capture");
        Call(LocalDisableA20, 0).Ax.Should().Be(0, "no local enable is left");
    }

    [Fact]
    public void StateReadBackFromJsonRestoresTheSameDriver() {
        // Arrange
        ushort handle = Allocate();
        WriteToBlock(handle, 0x42);
        string json = JsonSerializer.Serialize(_xms.CaptureState());

        // Act
        WriteToBlock(handle, 0x99);
        Call(FreeExtendedMemoryBlock, handle);
        XmsState? readBack = JsonSerializer.Deserialize<XmsState>(json);
        _xms.RestoreState(readBack ?? throw new InvalidOperationException("The state did not deserialize."));

        // Assert
        ReadFromBlock(handle).Should().Be(0x42);
        Allocate().Should().Be((ushort)(handle + 1));
    }

    private ushort Allocate() {
        (ushort ax, _, ushort dx) = Call(AllocateExtendedMemoryBlock, 1);
        ax.Should().Be(1);
        return dx;
    }

    private (ushort Ax, byte Bl, ushort Dx) Call(byte function, ushort dx) {
        _state.AH = function;
        _state.DX = dx;
        _xms.RunMultiplex();
        return (_state.AX, _state.BL, _state.DX);
    }

    private void WriteToBlock(ushort handle, byte value) {
        _memory.UInt8[ConventionalBufferOffset] = value;
        Move(0, ConventionalBufferOffset, handle, 0);
    }

    private byte ReadFromBlock(ushort handle) {
        _memory.UInt8[ConventionalBufferOffset] = 0;
        Move(handle, 0, 0, ConventionalBufferOffset);
        return _memory.UInt8[ConventionalBufferOffset];
    }

    private void Move(ushort sourceHandle, uint sourceOffset, ushort destinationHandle, uint destinationOffset) {
        ExtendedMemoryMoveStructure move = new(_memory, MoveStructureAddress) {
            Length = 2,
            SourceHandle = sourceHandle,
            SourceOffset = sourceOffset,
            DestHandle = destinationHandle,
            DestOffset = destinationOffset
        };
        _state.DS = 0;
        _state.SI = (ushort)MoveStructureAddress;
        Call(MoveExtendedMemoryBlock, 0).Ax.Should().Be(1, $"the move of {move.Length} bytes succeeds");
    }
}
