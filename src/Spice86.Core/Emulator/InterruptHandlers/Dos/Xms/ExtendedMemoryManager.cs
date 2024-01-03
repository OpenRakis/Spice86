namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

using Spice86.Core;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.Core.Emulator.OperatingSystem;

using System;
using System.Collections.Generic;
using System.Linq;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Provides DOS applications with XMS memory.
/// <remarks>This provides XMS 2.0</remarks>
/// </summary>
public sealed class ExtendedMemoryManager : InterruptHandler, IMemoryDevice {
    private int _a20EnableCount;
    private readonly LinkedList<XmsBlock> _xmsBlocksLinkedList = new();
    private readonly SortedList<int, int> _xmsHandles = new();
    
    /// <summary>
    /// The segment of the interrupt handler.
    /// </summary>

    public const ushort DosDeviceSegment = 0xD000;
    
    /// <summary>
    /// The size of available XMS Memory, in bytes.
    /// <remarks>
    /// 32 MB for XMS 2.0
    /// </remarks>
    /// </summary>
    public const uint XmsMemorySize = 32 * 1024 * 1024;

    /// <summary>
    /// XMS plain old memory.
    /// </summary>
    public Ram XmsRam { get; private set; } = new(XmsMemorySize);

    /// <summary>
    /// DOS Device Driver Name.
    /// </summary>
    public const string XmsIdentifier = "XMSXXXX0";

    public ExtendedMemoryManager(IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider,
        Stack stack, State state, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        Memory.LoadData(MemoryUtils.ToPhysicalAddress(DosDeviceSegment, 0),
            new byte[]{
                0xEB, // jump near
                0x03, // offset
                0x90, // NOP
                0x90, // NOP
                0x90 // NOP
            });
        Memory.RegisterMapping(XmsBaseAddress, XmsMemorySize, this);
        _xmsBlocksLinkedList.AddFirst(new XmsBlock(0, 0, XmsMemorySize, false));
        FillDispatchTable();
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

    /// <inheritdoc/>
    public override byte VectorNumber => 0x43;

    private void FillDispatchTable() {
        AddAction(0x00, GetVersionNumber);
        AddAction(0x01, RequestHighMemoryArea);
        AddAction(0x02, ReleaseHighMemoryArea);
        AddAction(0x03, GlobalEnableA20);
        AddAction(0x04, GlobalDisableA20);
        AddAction(0x05, EnableLocalA20);
        AddAction(0x06, DisableLocalA20);
        AddAction(0x07, QueryA20);
        AddAction(0x08, QueryFreeExtendedMemory);
        AddAction(0x09, AllocateExtendedMemoryBlock);
        AddAction(0x10, RequestUpperMemoryBlock);
        AddAction(0x0A, FreeExtendedMemoryBlock);
        AddAction(0x0B, MoveExtendedMemoryBlock);
        AddAction(0x0C, LockExtendedMemoryBlock);
        AddAction(0x0D, UnlockExtendedMemoryBlock);
        AddAction(0x0E, GetHandleInformation);
        AddAction(0x88, QueryAnyFreeExtendedMemory);
        AddAction(0x89, () => AllocateAnyExtendedMemory(State.EDX));
    }

    /// <inheritdoc/>
    public override void Run() {
        byte operation = State.AH;
        Run(operation);
    }

    public void GetVersionNumber() {
        State.AX = 0x0200; // Return version 2.00
        State.BX = 0; // Internal version
        State.DX = 1; // HMA exists
    }

    public void RequestHighMemoryArea() {
        State.AX = 0; // Didn't work
        State.BL = 0x91; // HMA already in use
    }

    public void ReleaseHighMemoryArea() {
        State.AX = 0; // Didn't work
        State.BL = 0x93; // HMA not allocated
    }

    public void QueryFreeExtendedMemory() {
        if (LargestFreeBlock <= ushort.MaxValue * 1024u) {
            State.AX = (ushort)(LargestFreeBlock / 1024u);
        } else {
            State.AX = ushort.MaxValue;
        }

        if (TotalFreeMemory <= ushort.MaxValue * 1024u) {
            State.DX = (ushort)(TotalFreeMemory / 1024);
        } else {
            State.DX = ushort.MaxValue;
        }

        if (State.AX == 0 && State.DX == 0) {
            State.BL = 0xA0;
        }
    }

    public void AllocateExtendedMemoryBlock() {
        AllocateAnyExtendedMemory(State.DX);
    }

    public void RequestUpperMemoryBlock() {
        State.BL = 0xB1; // No UMB's available.
        State.AX = 0; // Didn't work.
    }

    public void GlobalDisableA20() {
        Memory.A20Gate.IsEnabled = false;
        State.AX = 1; // Success
    }

    public void GlobalEnableA20() {
        Memory.A20Gate.IsEnabled = true;
        State.AX = 1; // Success
    }

    public void QueryA20() {
        State.AX = (ushort)(_a20EnableCount > 0 ? (short)1 : (short)0);
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
            Memory.A20Gate.IsEnabled = true;
        }
        _a20EnableCount++;
        State.AX = 1; // Success
    }

    /// <summary>
    /// Decrements the A20 enable count.
    /// </summary>
    public void DisableLocalA20() {
        if (_a20EnableCount == 1) {
            Memory.A20Gate.IsEnabled = false;
        }

        if (_a20EnableCount > 0) {
            _a20EnableCount--;
        }
        State.AX = 1; // Success
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
            State.AX = 1; // Success.
            State.DX = (ushort)handle;
        } else {
            State.AX = 0; // Didn't work.
            State.BL = res;
        }
    }

    /// <summary>
    /// Frees a block of memory.
    /// </summary>
    public void FreeExtendedMemoryBlock() {
        int handle = State.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            State.AX = 0; // Didn't work.
            State.BL = 0xA2; // Invalid handle.
            return;
        }

        if (lockCount > 0) {
            State.AX = 0; // Didn't work.
            State.BL = 0xAB; // Handle is locked.
            return;
        }

        if (TryGetBlock(handle, out XmsBlock block)) {
            XmsBlock freeBlock = block.Free();
            _xmsBlocksLinkedList.Replace(block, freeBlock);
            MergeFreeBlocks(freeBlock);
        }

        _xmsHandles.Remove(handle);
        State.AX = 1; // Success.
    }

    /// <summary>
    /// Locks a block of memory.
    /// </summary>
    public void LockExtendedMemoryBlock() {
        int handle = State.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            State.AX = 0; // Didn't work.
            State.BL = 0xA2; // Invalid handle.
            return;
        }

        _xmsHandles[handle] = lockCount + 1;

        _ = TryGetBlock(handle, out XmsBlock block);
        uint fullAddress = XmsBaseAddress + block.Offset;

        State.AX = 1; // Success.
        State.DX = (ushort)(fullAddress >> 16);
        State.BX = (ushort)(fullAddress & 0xFFFFu);
    }

    /// <summary>
    /// Unlocks a block of memory.
    /// </summary>
    public void UnlockExtendedMemoryBlock() {
        int handle = State.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            State.AX = 0; // Didn't work.
            State.BL = 0xA2; // Invalid handle.
            return;
        }

        if (lockCount < 1) {
            State.AX = 0;
            State.BL = 0xAA; // Handle is not locked.
            return;
        }

        _xmsHandles[handle] = lockCount - 1;

        State.AX = 1; // Success.
    }

    /// <summary>
    /// Returns information about an XMS handle.
    /// </summary>
    public void GetHandleInformation() {
        int handle = State.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            State.AX = 0; // Didn't work.
            State.BL = 0xA2; // Invalid handle.
            return;
        }

        State.BH = (byte)lockCount;
        State.BL = (byte)(MaxHandles - _xmsHandles.Count);

        if (!TryGetBlock(handle, out XmsBlock block)) {
            State.DX = 0;
        } else {
            State.DX = (ushort)(block.Length / 1024u);
        }

        State.AX = 1; // Success.
    }

    /// <summary>
    /// Copies a block of memory.
    /// TODO: Verify this works
    /// </summary>
    public unsafe void MoveExtendedMemoryBlock() {
        bool a20State = Memory.A20Gate.IsEnabled;
        Memory.A20Gate.IsEnabled = true;

        var moveDataSpan = Memory.GetSpan(State.DS, State.SI);
        fixed (byte* moveDataPtr = moveDataSpan) {
            XmsMoveData moveData = *(XmsMoveData*)moveDataPtr;
            Span<byte> srcPtr = new byte[] { };
            Span<byte> destPtr = new byte[] { };

            if (moveData.SourceHandle == 0) {
                SegmentedAddress srcAddress = moveData.SourceAddress;
                srcPtr = Memory.GetSpan(srcAddress.Segment, srcAddress.Offset);
            } else {
                if (TryGetBlock(moveData.SourceHandle, out XmsBlock srcBlock)) {
                    srcPtr = Memory.GetSpan((int)(XmsBaseAddress + srcBlock.Offset + moveData.SourceOffset),
                        0);
                }
            }

            if (moveData.DestHandle == 0) {
                SegmentedAddress destAddress = moveData.DestAddress;
                destPtr = Memory.GetSpan(destAddress.Segment, destAddress.Offset);
            } else {
                if (TryGetBlock(moveData.DestHandle, out XmsBlock destBlock)) {
                    destPtr = Memory.GetSpan((int)(XmsBaseAddress + destBlock.Offset + moveData.DestOffset),
                        0);
                }
            }

            if (srcPtr.Length == 0) {
                State.BL = 0xA3; // Invalid source handle.
                State.AX = 0; // Didn't work.
                return;
            }

            if (destPtr.Length == 0) {
                State.BL = 0xA5; // Invalid destination handle.
                State.AX = 0; // Didn't work.
                return;
            }

            srcPtr.CopyTo(destPtr);

            State.AX = 1; // Success.
            Memory.A20Gate.IsEnabled = a20State;
        }
    }

    /// <summary>
    /// Queries free memory using 32-bit registers.
    /// </summary>
    public void QueryAnyFreeExtendedMemory() {
        State.EAX = LargestFreeBlock / 1024u;
        State.ECX = (uint)(XmsMemorySize - 1);
        State.EDX = (uint)(TotalFreeMemory / 1024);

        if (State.EAX == 0) {
            State.BL = 0xA0;
        } else {
            State.BL = 0;
        }
    }

    /// <inheritdoc/>
    public uint Size => XmsMemorySize;

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
}