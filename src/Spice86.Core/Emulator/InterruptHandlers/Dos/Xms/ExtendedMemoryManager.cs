namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

using Spice86.Core;
using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Provides DOS applications with XMS memory.
/// TODO: Remove dependency on main memory.
/// TODO: Remove unsafe code
/// </summary>
public class ExtendedMemoryManager : InterruptHandler, IDeviceCallbackProvider {
    private SegmentedAddress _callbackAddress;
    private int _a20EnableCount;
    private readonly LinkedList<XmsBlock> _xmsBlocksLinkedList = new();
    private readonly SortedList<int, int> _xmsHandles = new();

    public ExtendedMemoryManager(Machine machine) : base(machine) {
        _callbackAddress = new(0, 0);
        _machine = machine;
        InitializeMemoryMap();
        FillDispatchTable();
    }

    public bool IsHookable => true;

    /// <summary>
    /// Specifies the starting physical address of XMS.
    /// </summary>
    public const uint XmsBaseAddress = Memory.ConvMemorySize + 65536 + 0x4000 + 1024 * 1024;

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
    /// Gets the total amount of extended memory.
    /// </summary>
    public int ExtendedMemorySize => _machine.Memory.MemorySize - (int)XmsBaseAddress;

    public override byte Index => 0x43;

    public SegmentedAddress CallbackAddress {
        set => _callbackAddress = value;
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x00, new Callback(0x00, GetVersionNumber));
        _dispatchTable.Add(0x01, new Callback(0x01, RequestHighMemoryArea));
        _dispatchTable.Add(0x02, new Callback(0x02, ReleaseHighMemoryArea));
        _dispatchTable.Add(0x03, new Callback(0x03, GlobalEnableA20));
        _dispatchTable.Add(0x04, new Callback(0x04, GlobalDisableA20));
        _dispatchTable.Add(0x05, new Callback(0x05, EnableLocalA20));
        _dispatchTable.Add(0x06, new Callback(0x06, DisableLocalA20));
        _dispatchTable.Add(0x07, new Callback(0x07, QueryA20));
        _dispatchTable.Add(0x08, new Callback(0x08, QueryFreeExtendedMemory));
        _dispatchTable.Add(0x09, new Callback(0x09, AllocateExtendedMemoryBlock));
        _dispatchTable.Add(0x10, new Callback(0x10, RequestUpperMemoryBlock));
        _dispatchTable.Add(0x0A, new Callback(0x0A, FreeExtendedMemoryBlock));
        _dispatchTable.Add(0x0B, new Callback(0x0B, MoveExtendedMemoryBlock));
        _dispatchTable.Add(0x0C, new Callback(0x0C, LockExtendedMemoryBlock));
        _dispatchTable.Add(0x0D, new Callback(0x0D, UnlockExtendedMemoryBlock));
        _dispatchTable.Add(0x0E, new Callback(0x0E, GetHandleInformation));
        _dispatchTable.Add(0x88, new Callback(0x88, QueryAnyFreeExtendedMemory));
        _dispatchTable.Add(0x89, new Callback(0x89, () => AllocateAnyExtendedMemory(_state.EDX)));
    }

    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    /// <summary>
    /// FIXME: Make this code used.
    /// </summary>
    public void RunOnnAL() {
        switch (_state.AL) {
            case XmsHandlerFunctions.InstallationCheck:
                _state.AL = 0x80;
                break;

            case XmsHandlerFunctions.GetCallbackAddress:
                _state.BX = _callbackAddress.Offset;
                _state.ES = _callbackAddress.Segment;
                break;

            default:
                throw new NotImplementedException($"XMS interrupt handler function {_state.AL:X2}h not implemented.");
        }
    }

    public void GetVersionNumber() {
        _state.AX = 0x0300; // Return version 3.00
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
            _state.AX = unchecked(ushort.MaxValue);
        }

        if (TotalFreeMemory <= ushort.MaxValue * 1024u) {
            _state.DX = (ushort)(TotalFreeMemory / 1024);
        } else {
            _state.DX = unchecked(ushort.MaxValue);
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
        _machine.Memory.EnableA20 = false;
        _state.AX = 1; // Success
    }

    public void GlobalEnableA20() {
        _machine.Memory.EnableA20 = true;
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
        foreach (XmsBlock b in _xmsBlocksLinkedList) {
            if (b.IsUsed && b.Handle == handle) {
                block = b;
                return true;
            }
        }

        block = default;
        return false;
    }

    /// <summary>
    /// Increments the A20 enable count.
    /// </summary>
    public void EnableLocalA20() {
        if (_a20EnableCount == 0) {
            _machine.Memory.EnableA20 = true;
        }
        _a20EnableCount++;
        _state.AX = 1; // Success
    }

    /// <summary>
    /// Decrements the A20 enable count.
    /// </summary>
    public void DisableLocalA20() {
        if (_a20EnableCount == 1) {
            _machine.Memory.EnableA20 = false;
        }

        if (_a20EnableCount > 0) {
            _a20EnableCount--;
        }
        _state.AX = 1; // Success
    }

    /// <summary>
    /// Initializes the internal memory map.
    /// </summary>
    /// <exception cref="UnrecoverableException">If xms is already initialized</exception>
    public void InitializeMemoryMap() {
        if (_xmsBlocksLinkedList.Count != 0) {
            throw new UnrecoverableException($"XMS already initialized, in {nameof(InitializeMemoryMap)}");
        }

        uint memoryAvailable = (uint)_machine.Memory.MemorySize - XmsBaseAddress;
        _xmsBlocksLinkedList.AddFirst(new XmsBlock(0, 0, memoryAvailable, false));
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
    /// </summary>
    public void MoveExtendedMemoryBlock() {
        bool a20State = _machine.Memory.EnableA20;
        _machine.Memory.EnableA20 = true;

        XmsMoveData moveData;
        unsafe {
            moveData = *(XmsMoveData*)_machine.Memory.GetPointer(_state.DS, _state.SI);
        }

        IntPtr srcPtr = IntPtr.Zero;
        IntPtr destPtr = IntPtr.Zero;

        if (moveData.SourceHandle == 0) {
            SegmentedAddress srcAddress = moveData.SourceAddress;
            srcPtr = _machine.Memory.GetPointer(srcAddress.Segment, srcAddress.Offset);
        } else {
            if (TryGetBlock(moveData.SourceHandle, out XmsBlock srcBlock)) {
                srcPtr = _machine.Memory.GetPointer((int)(XmsBaseAddress + srcBlock.Offset + moveData.SourceOffset));
            }
        }

        if (moveData.DestHandle == 0) {
            SegmentedAddress destAddress = moveData.DestAddress;
            destPtr = _machine.Memory.GetPointer(destAddress.Segment, destAddress.Offset);
        } else {
            if (TryGetBlock(moveData.DestHandle, out XmsBlock destBlock)) {
                destPtr = _machine.Memory.GetPointer((int)(XmsBaseAddress + destBlock.Offset + moveData.DestOffset));
            }
        }

        if (srcPtr == IntPtr.Zero) {
            _state.BL = 0xA3; // Invalid source handle.
            _state.AX = 0; // Didn't work.
            return;
        }
        if (destPtr == IntPtr.Zero) {
            _state.BL = 0xA5; // Invalid destination handle.
            _state.AX = 0; // Didn't work.
            return;
        }

        unsafe {
            byte* src = (byte*)srcPtr.ToPointer();
            byte* dest = (byte*)destPtr.ToPointer();

            for (uint i = 0; i < moveData.Length; i++) {
                dest[i] = src[i];
            }
        }

        _state.AX = 1; // Success.
        _machine.Memory.EnableA20 = a20State;
    }

    /// <summary>
    /// Queries free memory using 32-bit registers.
    /// </summary>
    public void QueryAnyFreeExtendedMemory() {
        _state.EAX = LargestFreeBlock / 1024u;
        _state.ECX = (uint)(_machine.Memory.MemorySize - 1);
        _state.EDX = (uint)(TotalFreeMemory / 1024);

        if (_state.EAX == 0) {
            _state.BL = 0xA0;
        } else {
            _state.BL = 0;
        }
    }
}