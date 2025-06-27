namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

using Spice86.Core;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

/// <summary>
/// Provides DOS applications with XMS memory. <br/>
/// XMS is always called through INT 0x2F, AH=0x43. <br/>
/// </summary>
/// <remarks>This provides XMS 2.0</remarks>
public sealed class ExtendedMemoryManager : IVirtualDevice, IMemoryDevice {
    private int _a20EnableCount;

    private readonly ILoggerService _loggerService;
    private readonly State _state;
    private readonly A20Gate _a20Gate;
    private readonly IMemory _memory;
    private readonly LinkedList<XmsBlock> _xmsBlocksLinkedList = new();
    private readonly SortedList<int, int> _xmsHandles = new();
    
    /// <summary>
    /// The segment of the interrupt handler.
    /// </summary>

    public const ushort DosDeviceSegment = 0xD000;

    /// <summary>
    /// The size of available XMS Memory, in kilobytes.
    /// </summary>
    /// <remarks>
    /// 32 MB for XMS 2.0
    /// </remarks>
    public const uint XmsMemorySize = 32 * 1024;

    /// <summary>
    /// XMS plain old memory.
    /// </summary>
    public Ram XmsRam { get; private set; } = new(XmsMemorySize * 1024);

    /// <summary>
    /// DOS Device Driver Name.
    /// </summary>
    public const string XmsIdentifier = "XMSXXXX0";

    public ExtendedMemoryManager(IMemory memory, A20Gate a20Gate,
        CallbackHandler callbackHandler, State state, ILoggerService loggerService) {
        Header = new DosDeviceHeader(memory,
            new SegmentedAddress(DosDeviceSegment, 0x0).Linear) {
            Name = XmsIdentifier,
            Attributes = DeviceAttributes.Ioctl,
            StrategyEntryPoint = 0,
            InterruptEntryPoint = 0
        };
        _state = state;
        _a20Gate = a20Gate;
        _memory = memory;
        _loggerService = loggerService;
        MemoryAsmWriter memoryAsmWriter = new(memory, new(DosDeviceSegment, 0), callbackHandler);
        memoryAsmWriter.WriteJumpNear(0x3);
        memoryAsmWriter.WriteNop();
        memoryAsmWriter.WriteNop();
        memoryAsmWriter.WriteNop();
        memoryAsmWriter.RegisterAndWriteCallback(0x43, RunMultiplex);
        memoryAsmWriter.WriteIret();
        memoryAsmWriter.WriteFarRet();
        memory.RegisterMapping(XmsBaseAddress, XmsMemorySize * 1024, this);
        _xmsBlocksLinkedList.AddFirst(new XmsBlock(0, 0, XmsMemorySize * 1024, false));
        Name = XmsIdentifier;
    }

    /// <summary>
    /// Specifies the starting physical address of XMS. XMS starts at 1088k after HMA
    /// </summary>
    public const uint XmsBaseAddress = 0x10FFF0;

    /// <summary>
    /// Total number of handles available at once.
    /// </summary>
    private const int MaxHandles = 128;

    /// <summary>
    /// Gets the largest free block of memory in bytes.
    /// </summary>
    public uint LargestFreeBlock => GetFreeBlocks().FirstOrDefault().Length;
    
    /// <summary>
    /// Gets the total amount of free memory in bytes.
    /// </summary>
    public long TotalFreeMemory => GetFreeBlocks().Sum(b => b.Length);

    /// <summary>
    /// Run XMS subfunctions in AL. XMS is called via INT2F with AH=0x43.<br/>
    /// INT2F is the DOS Multiplex interrupts handler.
    /// //TODO: Expand INT2F ! Especially regarding IOCTL.
    /// </summary>
    public void RunMultiplex() {
        byte operation = _state.AL;
        switch (operation) {
            case 0x00:
                GetVersionNumber();
                break;
            case 0x01:
                RequestHighMemoryArea();
                break;
            case 0x02:
                ReleaseHighMemoryArea();
                break;
            case 0x03:
                GlobalEnableA20();
                break;
            case 0x04:
                GlobalDisableA20();
                break;
            case 0x05:
                EnableLocalA20();
                break;
            case 0x06:
                DisableLocalA20();
                break;
            case 0x07:
                QueryA20();
                break;
            case 0x08:
                QueryFreeExtendedMemory();
                break;
            case 0xA:
                FreeExtendedMemoryBlock();
                break;
            case 0xB:
                MoveExtendedMemoryBlock();
                break;
            case 0xC:
                LockExtendedMemoryBlock();
                break;
            case 0xD:
                UnlockExtendedMemoryBlock();
                break;
            case 0xE:
                GetHandleInformation();
                break;
            case 0xF:
                ReallocateExtendedMemoryBlock();
                break;
            case 0x10:
                RequestUpperMemoryBlock();
                break;
            case 0x11:
                ReleaseUpperMemoryBlock();
                break;
        }
    }

    private void ReleaseUpperMemoryBlock() {
        throw new NotImplementedException();
    }

    private void ReallocateExtendedMemoryBlock() {
        throw new NotImplementedException();
    }

    public void GetVersionNumber() {
        _state.AX = 0x0200; // Return version 2.00
        _state.BX = 0; // Internal version
        _state.DX = 1; // HMA exists
    }

    public void RequestHighMemoryArea() {
        _state.AX = 0; // Didn't work
        _state.BL = 0x91; // HMA already in use
    }

    public void ReleaseHighMemoryArea() {
        _state.AX = 0; // Didn't work
        _state.BL = 0x93; // HMA not allocated
    }

    public void QueryFreeExtendedMemory() {
        if (LargestFreeBlock <= ushort.MaxValue * 1024u) {
            _state.AX = (ushort)(LargestFreeBlock / 1024u);
        } else {
            _state.AX = ushort.MaxValue;
        }

        if (TotalFreeMemory <= ushort.MaxValue * 1024u) {
            _state.DX = (ushort)(TotalFreeMemory / 1024);
        } else {
            _state.DX = ushort.MaxValue;
        }

        if (_state.AX == 0 && _state.DX == 0) {
            _state.BL = 0xA0;
        }
    }

    public void AllocateExtendedMemoryBlock() {
        AllocateAnyExtendedMemory(_state.DX);
    }

    public void RequestUpperMemoryBlock() {
        _state.BL = 0xB1; // No UMB's available.
        _state.AX = 0; // Didn't work.
    }

    public void GlobalDisableA20() {
        _memory.A20Gate.IsEnabled = false;
        _state.AX = 1; // Success
    }

    public void GlobalEnableA20() {
        _memory.A20Gate.IsEnabled = true;
        _state.AX = 1; // Success
    }

    public void QueryA20() {
        _state.AX = (ushort)(_a20EnableCount > 0 ? (short)1 : (short)0);
    }

    /// <summary>
    /// Attempts to allocate a block of extended memory.
    /// </summary>
    /// <param name="length">Number of bytes to allocate.</param>
    /// <param name="handle">If successful, contains the allocation handle.</param>
    /// <returns>Zero on success. Nonzero indicates error code.</returns>
    public byte TryAllocate(uint length, out short handle) {
        handle = (short)GetNextHandle();
        if (handle == 0) {
            return 0xA1; // All handles are used.
        }

        // Round up to next kbyte if necessary.
        if (length % 1024 != 0) {
            length = (length & 0xFFFFFC00u) + 1024u;
        } else {
            length &= 0xFFFFFC00u;
        }

        // Zero-length allocations are allowed.
        if (length == 0) {
            _xmsHandles.Add(handle, 0);
            return 0;
        }

        XmsBlock? smallestFreeBlock = GetFreeBlocks()
            .Where(b => b.Length >= length)
            .Select(static b => new XmsBlock?(b))
            .FirstOrDefault();

        if (smallestFreeBlock == null) {
            return 0xA0; // Not enough free memory.
        }

        LinkedListNode<XmsBlock>? freeNode = _xmsBlocksLinkedList.Find(smallestFreeBlock.Value);
        if (freeNode is not null) {
            XmsBlock[] newNodes = freeNode.Value.Allocate(handle, length);
            _xmsBlocksLinkedList.Replace((XmsBlock)smallestFreeBlock, newNodes);
        }

        _xmsHandles.Add(handle, 0);
        return 0;
    }

    /// <summary>
    /// Returns the block with the specified handle if found; otherwise returns null.
    /// </summary>
    /// <param name="handle">Handle of block to search for.</param>
    /// <param name="block">On success, contains information about the block.</param>
    /// <returns>True if handle was found; otherwise false.</returns>
    public bool TryGetBlock(int handle, out XmsBlock block) {
        foreach (XmsBlock b in _xmsBlocksLinkedList.Where(b => b.IsUsed && b.Handle == handle)) {
            block = b;
            return true;
        }

        block = default;
        return false;
    }

    /// <summary>
    /// Increments the A20 enable count.
    /// </summary>
    public void EnableLocalA20() {
        if (_a20EnableCount == 0) {
            _memory.A20Gate.IsEnabled = true;
        }
        _a20EnableCount++;
        _state.AX = 1; // Success
    }

    /// <summary>
    /// Decrements the A20 enable count.
    /// </summary>
    public void DisableLocalA20() {
        if (_a20EnableCount == 1) {
            _memory.A20Gate.IsEnabled = false;
        }

        if (_a20EnableCount > 0) {
            _a20EnableCount--;
        }
        _state.AX = 1; // Success
    }
    
    /// <summary>
    /// Returns all of the free blocks in the map sorted by size in ascending order.
    /// </summary>
    /// <returns>Sorted list of free blocks in the map.</returns>
    public IEnumerable<XmsBlock> GetFreeBlocks() => _xmsBlocksLinkedList.Where(static x => !x.IsUsed).OrderBy(static x => x.Length);

    /// <summary>
    /// Returns the next available handle for an allocation on success; returns 0 if no handles are available.
    /// </summary>
    /// <returns>New handle if available; otherwise returns null.</returns>
    public int GetNextHandle() {
        for (int i = 1; i <= MaxHandles; i++) {
            if (!_xmsHandles.ContainsKey(i)) {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Attempts to merge a free block with the following block if possible.
    /// </summary>
    /// <param name="firstBlock">Free block to merge.</param>
    public void MergeFreeBlocks(XmsBlock firstBlock) {
        LinkedListNode<XmsBlock>? firstNode = _xmsBlocksLinkedList.Find(firstBlock);

        if (firstNode?.Next != null) {
            LinkedListNode<XmsBlock> nextNode = firstNode.Next;
            if (!nextNode.Value.IsUsed) {
                XmsBlock newBlock = firstBlock.Join(nextNode.Value);
                _xmsBlocksLinkedList.Remove(nextNode);
                _xmsBlocksLinkedList.Replace(firstBlock, newBlock);
            }
        }
    }

    /// <summary>
    /// Allocates a new block of memory.
    /// </summary>
    /// <param name="kbytes">Number of kilobytes requested.</param>
    public void AllocateAnyExtendedMemory(uint kbytes) {
        byte res = TryAllocate(kbytes * 1024u, out short handle);
        if (res == 0) {
            _state.AX = 1; // Success.
            _state.DX = (ushort)handle;
        } else {
            _state.AX = 0; // Didn't work.
            _state.BL = res;
        }
    }

    /// <summary>
    /// Frees a block of memory.
    /// </summary>
    public void FreeExtendedMemoryBlock() {
        int handle = _state.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            _state.AX = 0; // Didn't work.
            _state.BL = 0xA2; // Invalid handle.
            return;
        }

        if (lockCount > 0) {
            _state.AX = 0; // Didn't work.
            _state.BL = 0xAB; // Handle is locked.
            return;
        }

        if (TryGetBlock(handle, out XmsBlock block)) {
            XmsBlock freeBlock = block.Free();
            _xmsBlocksLinkedList.Replace(block, freeBlock);
            MergeFreeBlocks(freeBlock);
        }

        _xmsHandles.Remove(handle);
        _state.AX = 1; // Success.
    }

    /// <summary>
    /// Locks a block of memory.
    /// </summary>
    public void LockExtendedMemoryBlock() {
        int handle = _state.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            _state.AX = 0; // Didn't work.
            _state.BL = 0xA2; // Invalid handle.
            return;
        }

        _xmsHandles[handle] = lockCount + 1;

        _ = TryGetBlock(handle, out XmsBlock block);
        uint fullAddress = XmsBaseAddress + block.Offset;

        _state.AX = 1; // Success.
        _state.DX = (ushort)(fullAddress >> 16);
        _state.BX = (ushort)(fullAddress & 0xFFFFu);
    }

    /// <summary>
    /// Unlocks a block of memory.
    /// </summary>
    public void UnlockExtendedMemoryBlock() {
        int handle = _state.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            _state.AX = 0; // Didn't work.
            _state.BL = 0xA2; // Invalid handle.
            return;
        }

        if (lockCount < 1) {
            _state.AX = 0;
            _state.BL = 0xAA; // Handle is not locked.
            return;
        }

        _xmsHandles[handle] = lockCount - 1;

        _state.AX = 1; // Success.
    }

    /// <summary>
    /// Returns information about an XMS handle.
    /// </summary>
    public void GetHandleInformation() {
        int handle = _state.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            _state.AX = 0; // Didn't work.
            _state.BL = 0xA2; // Invalid handle.
            return;
        }

        _state.BH = (byte)lockCount;
        _state.BL = (byte)(MaxHandles - _xmsHandles.Count);

        if (!TryGetBlock(handle, out XmsBlock block)) {
            _state.DX = 0;
        } else {
            _state.DX = (ushort)(block.Length / 1024u);
        }

        _state.AX = 1; // Success.
    }

    /// <summary>
    /// Copies a block of memory.
    /// TODO: Verify this works
    /// </summary>
    public unsafe void MoveExtendedMemoryBlock() {
        bool a20State = _memory.A20Gate.IsEnabled;
        _memory.A20Gate.IsEnabled = true;

        uint address = MemoryUtils.ToPhysicalAddress(_state.DS, _state.DI);
        ExtendedMemoryMoveStructure moveData = new(_memory, address);
        Span<byte> srcPtr = new byte[] { };
        Span<byte> destPtr = new byte[] { };

        if (moveData.SourceHandle == 0) {
            SegmentedAddress srcAddress = moveData.SourceAddress;
            srcPtr = _memory.GetSpan(srcAddress.Segment, srcAddress.Offset);
        } else {
            if (TryGetBlock(moveData.SourceHandle, out XmsBlock srcBlock)) {
                srcPtr = _memory.GetSpan((int)(XmsBaseAddress + srcBlock.Offset + moveData.SourceOffset),
                    0);
            }
        }

        if (moveData.DestHandle == 0) {
            SegmentedAddress destAddress = moveData.DestAddress;
            destPtr = _memory.GetSpan(destAddress.Segment, destAddress.Offset);
        } else {
            if (TryGetBlock(moveData.DestHandle, out XmsBlock destBlock)) {
                destPtr = _memory.GetSpan((int)(XmsBaseAddress + destBlock.Offset + moveData.DestOffset),
                    0);
            }
        }

        if (srcPtr.Length == 0) {
            _state.BL = 0xA3; // Invalid source handle.
            _state.AX = 0; // Didn't work.
            return;
        }

        if (destPtr.Length == 0) {
            _state.BL = 0xA5; // Invalid destination handle.
            _state.AX = 0; // Didn't work.
            return;
        }

        srcPtr.CopyTo(destPtr);

        _state.AX = 1; // Success.
        _memory.A20Gate.IsEnabled = a20State;
    }

    /// <inheritdoc/>
    public uint Size => XmsMemorySize * 1024;

    public uint DeviceNumber { get; set; }
    public DosDeviceHeader Header { get; init; }
    public ushort Information { get; }
    public string Name { get; set; }

    /// <inheritdoc/>
    public byte Read(uint address) {
        return XmsRam.Read(address - XmsBaseAddress);
    }

    /// <inheritdoc/>
    public void Write(uint address, byte value) {
        XmsRam.Write(address - XmsBaseAddress, value);
    }

    /// <inheritdoc/>
    public Span<byte> GetSpan(int address, int length) {
        return XmsRam.GetSpan((int) (address - XmsBaseAddress), length);
    }

    public byte GetStatus(bool inputFlag) {
        throw new NotImplementedException();
    }

    public bool TryReadFromControlChannel(uint address, ushort size, [NotNullWhen(true)] out ushort? returnCode) {
        throw new NotImplementedException();
    }

    public bool TryWriteToControlChannel(uint address, ushort size, [NotNullWhen(true)] out ushort? returnCode) {
        throw new NotImplementedException();
    }

    public void CopyExtendedMemory(bool calledFromVm) {
        throw new NotImplementedException();
    }
}