﻿namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

using System.Linq;

/// <summary>
/// Provides DOS applications with EMS memory.
/// </summary>
public sealed class ExpandedMemoryManager : InterruptHandler {
    /// <summary>
    /// Size of each EMS page in bytes.
    /// </summary>
    public const int PageSize = 16384;

    /// <summary>
    /// Maximum number of mappable pages.
    /// </summary>
    public const int MaximumPhysicalPages = 4;

    /// <summary>
    /// Maximum number of logical pages.
    /// </summary>
    public const int MaximumLogicalPages = 256;

    public const ushort PageFrameSegment = 0xE000;
    private const int FirstHandle = 1;
    private const int LastHandle = 254;
    private const int SystemHandle = 0;

    public const string EmsIdentifier = "EMMXXXX0";

    public EmsMemoryMapper EmsMemoryMapper { get; init; }

    public Memory EmsRam { get; init; } = new(8 * 1024);

    private readonly short[] pageOwners = new short[MaximumLogicalPages];
    private readonly SortedList<int, EmsHandle> handles = new();
    private readonly int[] mappedPages = new int[MaximumPhysicalPages] { -1, -1, -1, -1 };
    public ExpandedMemoryManager(Machine machine) : base(machine) {
        EmsMemoryMapper = new(_machine.Memory, MemoryUtils.ToPhysicalAddress(PageFrameSegment, 0));
        EmsMemoryMapper.SetZeroTerminatedString(MemoryUtils.ToPhysicalAddress(0xF100 - PageFrameSegment, 0x000A), EmsIdentifier, EmsIdentifier.Length + 1);

        pageOwners.AsSpan().Fill(-1);

        for (int i = 0; i < 24; i++) {
            pageOwners[i] = SystemHandle;
        }

        handles[SystemHandle] = new EmsHandle(Enumerable.Range(0, 24).Select(i => (ushort)i));
        FillDispatchTable();
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x40, new Callback(0x40, GetStatus));
        _dispatchTable.Add(0x41, new Callback(0x41, GetPageFrameAddress));
        _dispatchTable.Add(0x42, new Callback(0x42, GetUnallocatedPageCount));
        _dispatchTable.Add(0x43, new Callback(0x43, AllocatePages));
        _dispatchTable.Add(0x44, new Callback(0x44, MapUnmapHandlePage));
        _dispatchTable.Add(0x45, new Callback(0x45, DeallocatePages));
        _dispatchTable.Add(0x46, new Callback(0x46, GetVersion));
        _dispatchTable.Add(0x47, new Callback(0x47, SavePageMap));
        _dispatchTable.Add(0x48, new Callback(0x48, RestorePageMap));
        _dispatchTable.Add(0x4B, new Callback(0x4B, GetHandleCount));
        _dispatchTable.Add(0x4C, new Callback(0x4C, GetHandlePages));
        _dispatchTable.Add(0x50, new Callback(0x50, AdvancedMap));
        _dispatchTable.Add(0x51, new Callback(0x51, ReallocatePages));
        _dispatchTable.Add(0x53, new Callback(0x53, HandleName));
        _dispatchTable.Add(0x57, new Callback(0x57, MoveExchange));
        _dispatchTable.Add(0x59, new Callback(0x59, GetHardwareInformation));
    }

    public void AdvancedMap() {
        switch (_state.AL) {
            case EmsFunctions.AdvancedMap_MapUnmapPages:
                MapUnmapMultiplePages();
                break;

            default:
                throw new UnrecoverableException();
        }
    }

    public void GetPageFrameAddress() {
        // Return page frame segment in BX.
        _state.BX = unchecked(PageFrameSegment);
        // Set good status.
        _state.AH = 0;
    }

    public void GetUnallocatedPageCount() {
        // Return number of pages available in BX.
        _state.BX = (ushort)(MaximumLogicalPages - AllocatedPages);
        // Return total number of pages in DX.
        _state.DX = MaximumLogicalPages;
        // Set good status.
        _state.AH = 0;
    }

    public void GetVersion() {
        // Return EMS version 4.0.
        _state.AL = 0x40;
        // Return good status.
        _state.AH = 0;
    }

    public void GetHardwareInformation() {
        switch (_state.AL) {
        case EmsFunctions.GetHardwareInformation_UnallocatedRawPages:
            // Return number of pages available in BX.
            _state.BX = (ushort)(MaximumLogicalPages - AllocatedPages);
            // Return total number of pages in DX.
            _state.DX = MaximumLogicalPages;
            // Set good status.
            _state.AH = 0;
            break;

        default:
            throw new UnrecoverableException();
        }
    }

    public void GetHandleCount() {
        // Return the number of EMM handles (plus 1 for the OS handle).
        _state.BX = (ushort)(handles.Count + 1);
        // Return good status.
        _state.AH = 0;
    }

    public void AdvanceMap() {
        switch (_state.AL) {
            case EmsFunctions.AdvancedMap_MapUnmapPages:
                MapUnmapMultiplePages();
                break;
            default:
                throw new UnrecoverableException();
        }
    }

    public void HandleName() {
        switch (_state.AL) {
            case EmsFunctions.HandleName_Get:
                GetHandleName();
                break;

            case EmsFunctions.HandleName_Set:
                SetHandleName();
                break;

            default:
                throw new UnrecoverableException();
        }
    }

    public void MoveExchange() {
        switch (_state.AL) {
            case EmsFunctions.MoveExchange_Move:
                Move();
                break;

            default:
                throw new NotImplementedException($"EMM function 57{_state.AL:X2}h not implemented.");
        }
    }

    /// <summary>
    /// Gets the total number of allocated EMS pages.
    /// </summary>
    public int AllocatedPages => handles.Values.Sum(p => p.PagesAllocated);

    /// <summary>
    /// Gets the mapped address in main memory for the current page
    /// </summary>
    public uint MappedAddress => MemoryUtils.ToPhysicalAddress(PageFrameSegment, 0);

    public override byte Index => 0x67;

    public void GetStatus() {
        // Return good status in AH.
        _state.AH = 0;
    }

    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    /// <summary>
    /// Allocates pages for a new handle.
    /// </summary>
    public void AllocatePages() {
        uint pagesRequested = _state.BX;
        if (pagesRequested == 0) {
            // Return "attempted to allocate zero pages" code.
            _state.AH = 0x89;
            return;
        }

        if (pagesRequested <= MaximumLogicalPages - AllocatedPages) {
            // Some programs like to use one more page than they ask for.
            // What a bunch of rubbish.
            int handle = CreateHandle((int)pagesRequested + 1);
            if (handle != 0) {
                // Return handle in DX.
                _state.DX = (ushort)handle;
                // Return good status.
                _state.AH = 0;
            } else {
                // Return "all handles in use" code.
                _state.AH = 0x85;
            }
        } else {
            // Return "not enough available pages" code.
            _state.AH = 0x87;
        }
    }

    /// <summary>
    /// Reallocates pages for a handle.
    /// </summary>
    public void ReallocatePages() {
        int pagesRequested = _state.BX;

        if (pagesRequested < MaximumLogicalPages) {
            int handle = _state.DX;
            if (handles.TryGetValue(handle, out EmsHandle? emsHandle)) {
                if (pagesRequested < emsHandle.PagesAllocated) {
                    for (int i = emsHandle.LogicalPages.Count - 1; i >= emsHandle.LogicalPages.Count - pagesRequested; i--) {
                        mappedPages[emsHandle.LogicalPages[i]] = -1;
                    }
                emsHandle.LogicalPages.RemoveRange(emsHandle.LogicalPages.Count - pagesRequested, emsHandle.PagesAllocated - pagesRequested);
                } else if (pagesRequested > emsHandle.PagesAllocated) {
                    int pagesToAdd = pagesRequested - emsHandle.PagesAllocated;
                    for (int i = 0; i < pagesToAdd; i++) {
                        ushort logicalPage = GetNextFreePage((short)handle);
                        emsHandle.LogicalPages.Add(logicalPage);
                    }
                }

                // Return good status.
                _state.AH = 0;
            } else {
                // Return "couldn't find specified handle" code.
                _state.AH = 0x83;
            }
        } else {
            // Return "not enough available pages" code.
            _state.AH = 0x87;
        }
    }

    /// <summary>
    /// Attempts to create a new EMS handle.
    /// </summary>
    /// <param name="pagesRequested">Number of pages to allocate to the new handle.</param>
    /// <returns>New EMS handle if created successfully; otherwise null.</returns>
    private int CreateHandle(int pagesRequested) {
        for (int i = FirstHandle; i <= LastHandle; i++) {
            if (!handles.ContainsKey(i)) {
                var pages = new List<ushort>(pagesRequested);
                for (int p = 0; p < pagesRequested; p++) {
                    pages.Add(GetNextFreePage((short)i));
                }
                var handle = new EmsHandle(pages);
                handles.Add(i, handle);
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Deallocates a handle and all of its pages.
    /// </summary>
    public void DeallocatePages() {
        int handle = _state.DX;
        if (handles.Remove(handle)) {
            for (int i = 0; i < pageOwners.Length; i++) {
                if (pageOwners[i] == handle) {
                    pageOwners[i] = -1;
                }
            }

            // Return good status.
            _state.AH = 0;
        } else {
            // Return "couldn't find specified handle" code.
            _state.AH = 0x83;
        }
    }

    /// <summary>
    /// Maps or unmaps a physical page.
    /// </summary>
    public void MapUnmapHandlePage() {
        int physicalPage = _state.AL;
        if (physicalPage is < 0 or >= MaximumPhysicalPages) {
            // Return "physical page out of range" code.
            _state.AH = 0x8B;
            return;
        }

        int handleIndex = _state.DX;
        if (!handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return "couldn't find specified handle" code.
            _state.AH = 0x83;
            return;
        }

        int logicalPageIndex = _state.BX;

        if (logicalPageIndex != 0xFFFF) {
            if (logicalPageIndex < 0 || logicalPageIndex >= handle.LogicalPages.Count) {
                // Return "logical page out of range" code.
                _state.AH = 0x8A;
                return;
            }

            MapPage(handle.LogicalPages[logicalPageIndex], physicalPage);
        } else {
            UnmapPage(physicalPage);
        }

        // Return good status.
        _state.AH = 0;
    }

    /// <summary>
    /// Copies data from a logical page to a physical page.
    /// </summary>
    /// <param name="logicalPage">Logical page to copy from.</param>
    /// <param name="physicalPageIndex">Index of physical page to copy to.</param>
    private void MapPage(int logicalPage, int physicalPageIndex) {
        // If the requested logical page is already mapped, it needs to get unmapped first.
        UnmapLogicalPage(logicalPage);

        // If a page is already mapped, make sure it gets unmapped first.
        UnmapPage(physicalPageIndex);

        Span<byte> pageFrame = this.GetMappedPage(physicalPageIndex);
        Span<byte> ems = this.GetLogicalPage(logicalPage);
        ems.CopyTo(pageFrame);
        mappedPages[physicalPageIndex] = logicalPage;
    }

    /// <summary>
    /// Copies data from a physical page to a logical page.
    /// </summary>
    /// <param name="physicalPageIndex">Physical page to copy from.</param>
    private void UnmapPage(int physicalPageIndex) {
        int currentPage = mappedPages[physicalPageIndex];
        if (currentPage != -1) {
            Span<byte> pageFrame = GetMappedPage(physicalPageIndex);
            Span<byte> ems = GetLogicalPage(currentPage);
            pageFrame.CopyTo(ems);
            mappedPages[physicalPageIndex] = -1;
        }
    }

    /// <summary>
    /// Unmaps a specific logical page if it is currently mapped.
    /// </summary>
    /// <param name="logicalPage">Logical page to unmap.</param>
    private void UnmapLogicalPage(int logicalPage) {
        for (int i = 0; i < mappedPages.Length; i++) {
            if (mappedPages[i] == logicalPage) {
                UnmapPage(i);
            }
        }
    }

    /// <summary>
    /// Gets the number of pages allocated to a handle.
    /// </summary>
    public void GetHandlePages() {
        int handleIndex = _state.DX;
        if (handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return the number of pages allocated in BX.
            _state.BX = (ushort)handle.PagesAllocated;
            // Return good status.
            _state.AH = 0;
        } else {
            // Return "couldn't find specified handle" code.
            _state.AH = 0x83;
        }
    }

    /// <summary>
    /// Gets the name of a handle.
    /// </summary>
    private void GetHandleName() {
        int handleIndex = _state.DX;
        if (handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Write the handle name to ES:DI.
            EmsMemoryMapper.SetZeroTerminatedString(MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI), handle.Name, handle.Name.Length + 1);
            // Return good status.
            _state.AH = 0;
        } else {
            // Return "couldn't find specified handle" code.
            _state.AH = 0x83;
        }
    }

    /// <summary>
    /// Set the name of a handle.
    /// </summary>
    private void SetHandleName() {
        int handleIndex = _state.DX;
        if (handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Read the handle name from DS:SI.
            handle.Name = EmsMemoryMapper.GetZeroTerminatedString(MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI), 8);
            // Return good status.
            _state.AH = 0;
        } else {
            // Return "couldn't find specified handle" code.
            _state.AH = 0x83;
        }
    }

    /// <summary>
    /// Maps or unmaps multiple pages.
    /// </summary>
    private void MapUnmapMultiplePages() {
        int handleIndex = _state.DX;
        if (!handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return "couldn't find specified handle" code.
            _state.AH = 0x83;
            return;
        }

        int pageCount = _state.CX;
        if (pageCount is < 0 or > MaximumPhysicalPages) {
            // Return "physical page count out of range" code.
            _state.AH = 0x8B;
            return;
        }

        ushort arraySegment = _state.DS;
        ushort arrayOffset = _state.SI;
        for (int i = 0; i < pageCount; i++) {
            ushort logicalPageIndex = EmsRam.GetUint16(MemoryUtils.ToPhysicalAddress(arraySegment, arrayOffset));
            ushort physicalPageIndex = EmsRam.GetUint16(MemoryUtils.ToPhysicalAddress(arraySegment, (ushort)(arrayOffset + 2u)));

            if (physicalPageIndex >= MaximumPhysicalPages) {
                // Return "physical page out of range" code.
                _state.AH = 0x8B;
                return;
            }

            if (logicalPageIndex != 0xFFFF) {
                if (logicalPageIndex >= handle.LogicalPages.Count) {
                    // Return "logical page out of range" code.
                    _state.AH = 0x8A;
                    return;
                }

                MapPage(handle.LogicalPages[logicalPageIndex], physicalPageIndex);
            } else {
                UnmapPage(physicalPageIndex);
            }

            arrayOffset = (ushort)(arrayOffset + 4u);
        }

        // Return good status.
        _state.AH = 0;
    }
    /// <summary>
    /// Saves the current state of page map registers for a handle.
    /// </summary>
    public void SavePageMap() {
        int handleIndex = _state.DX;
        if (!handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return "couldn't find specified handle" code.
            _state.AH = 0x83;
            return;
        }

        mappedPages.CopyTo(handle.SavedPageMap);

        // Return good status.
        _state.AH = 0;
    }
    /// <summary>
    /// Restores the state of page map registers for a handle.
    /// </summary>
    public void RestorePageMap() {
        int handleIndex = _state.DX;
        if (!handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return "couldn't find specified handle" code.
            _state.AH = 0x83;
            return;
        }

        for (int i = 0; i < MaximumPhysicalPages; i++) {
            if (handle.SavedPageMap[i] != mappedPages[i]) {
                MapPage(handle.SavedPageMap[i], i);
            }
        }

        // Return good status.
        _state.AH = 0;
    }
    /// <summary>
    /// Copies a block of memory.
    /// </summary>
    private void Move() {
        int length = (int)_machine.Memory.GetUint32(MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI));

        byte sourceType = _machine.Memory.GetUint8(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 4u)));
        int sourceHandleIndex = _machine.Memory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 5u)));
        int sourceOffset = _machine.Memory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 7u)));
        int sourcePage = _machine.Memory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 9u)));

        byte destType = _machine.Memory.GetUint8(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 11u)));
        int destHandleIndex = _machine.Memory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 12u)));
        int destOffset = _machine.Memory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 14u)));
        int destPage = _machine.Memory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 16u)));

        SyncToEms();

        if (sourceType == 0 && destType == 0) {
            _state.AH = ConventionalMemoryConventionalMemory((uint)((sourcePage << 4) + sourceOffset), (uint)((destPage << 4) + destOffset), length);
        } else if (sourceType != 0 && destType == 0) {
            if (!handles.TryGetValue(sourceHandleIndex, out _)) {
                // Return "couldn't find specified handle" code.
                _state.AH = 0x83;
                return;
            }

            _state.AH = EmsToConventionalMemory(sourcePage, sourceOffset, (uint)((destPage << 4) + destOffset), length);
        } else if (sourceType == 0 && destType != 0) {
            if (!handles.TryGetValue(destHandleIndex, out _)) {
                // Return "couldn't find specified handle" code.
                _state.AH = 0x83;
                return;
            }

            _state.AH = ConvToEms((uint)((sourcePage << 4) + sourceOffset), destPage, destOffset, length);
        } else {
            if (!handles.TryGetValue(sourceHandleIndex, out EmsHandle? sourceHandle) || !handles.TryGetValue(destHandleIndex, out EmsHandle? destHandle)) {
                // Return "couldn't find specified handle" code.
                _state.AH = 0x83;
                return;
            }

            _state.AH = EmsToEms(sourceHandle, sourcePage, sourceOffset, destHandle, destPage, destOffset, length);
        }

        SyncFromEms();
    }
    /// <summary>
    /// Copies data from mapped conventional memory to EMS pages.
    /// </summary>
    private void SyncToEms() {
        for (int i = 0; i < MaximumPhysicalPages; i++) {
            if (mappedPages[i] != -1) {
                Span<byte> src = GetMappedPage(i);
                Span<byte> dest = GetLogicalPage(mappedPages[i]);
                src.CopyTo(dest);
            }
        }
    }
    /// <summary>
    /// Copies data from EMS pages to mapped conventional memory.
    /// </summary>
    private void SyncFromEms() {
        for (int i = 0; i < MaximumPhysicalPages; i++) {
            if (mappedPages[i] != -1) {
                Span<byte> src = GetLogicalPage(mappedPages[i]);
                Span<byte> dest = GetMappedPage(i);
                src.CopyTo(dest);
            }
        }
    }

    private Span<byte> GetMappedPage(int physicalPageIndex) => EmsRam.GetSpan(0, _machine.Memory.Ram.Length).Slice((PageFrameSegment << 4) + physicalPageIndex * PageSize, PageSize);

    private Span<byte> GetLogicalPage(int logicalPageIndex) => _machine.Memory.GetSpan(0, _machine.Memory.Ram.Length).Slice(logicalPageIndex * PageSize, PageSize);
    private ushort GetNextFreePage(short handle) {
        for (int i = 0; i < pageOwners.Length; i++) {
            if (pageOwners[i] == -1) {
                pageOwners[i] = handle;
                return (ushort)i;
            }
        }

        return 0;
    }

    private byte ConventionalMemoryConventionalMemory(uint sourceAddress, uint destAddress, int length) {
        if (length < 0) {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length == 0) {
            return 0;
        }

        if (sourceAddress + length > Memory.ConvMemorySize || destAddress + length > Memory.ConvMemorySize) {
            return 0xA2;
        }

        bool overlap = sourceAddress + length - 1 >= destAddress || destAddress + length - 1 >= sourceAddress;
        bool reverse = overlap && sourceAddress > destAddress;
        Memory memory = _machine.Memory;

        if (!reverse) {
            for (uint offset = 0; offset < length; offset++) {
                EmsRam.SetUint8(destAddress + offset, EmsRam.GetUint8(sourceAddress + offset));
            }
        } else {
            for (int offset = length - 1; offset >= 0; offset--) {
                EmsRam.SetUint8(destAddress + (uint)offset, EmsRam.GetUint8(sourceAddress + (uint)offset));
            }
        }

        return overlap ? (byte)0x92 : (byte)0;
    }
    private byte EmsToConventionalMemory(int sourcePage, int sourcePageOffset, uint destAddress, int length) {
        if (length < 0) {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length == 0) {
            return 0;
        }

        if (destAddress + length > Memory.ConvMemorySize) {
            return 0xA2;
        }

        if (sourcePageOffset >= PageSize) {
            return 0x95;
        }

        int offset = sourcePageOffset;
        uint sourceCount = destAddress;
        int pageIndex = sourcePage;
        while (length > 0) {
            int size = Math.Min(length, PageSize - offset);
            Span<byte> source = GetLogicalPage(pageIndex);
            if (source.IsEmpty) {
                return 0x8A;
            }

            for (int i = 0; i < size; i++) {
                EmsRam.SetUint8(sourceCount++, source[offset + i]);
            }

            length -= size;
            pageIndex++;
            offset = 0;
        }

        return 0;
    }
    private byte ConvToEms(uint sourceAddress, int destPage, int destPageOffset, int length) {
        if (length < 0) {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length == 0) {
            return 0;
        }

        if (sourceAddress + length > Memory.ConvMemorySize) {
            return 0xA2;
        }

        if (destPageOffset >= PageSize) {
            return 0x95;
        }

        int offset = destPageOffset;
        uint sourceCount = sourceAddress;
        int pageIndex = destPage;
        while (length > 0) {
            int size = Math.Min(length, PageSize - offset);
            Span<byte> target = GetLogicalPage(pageIndex);
            if (target.IsEmpty) {
                return 0x8A;
            }

            for (int i = 0; i < size; i++) {
                target[offset + i] = EmsRam.GetUint8(sourceCount++);
            }

            length -= size;
            pageIndex++;
            offset = 0;
        }

        return 0;
    }
    private byte EmsToEms(EmsHandle srcHandle, int sourcePage, int sourcePageOffset, EmsHandle destHandle, int destPage, int destPageOffset, int length) {
        if (length < 0) {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length == 0) {
            return 0;
        }

        if (sourcePageOffset >= PageSize || destPageOffset >= PageSize) {
            return 0x95;
        }

        bool overlap = false;
        bool reverse = false;

        if (srcHandle == destHandle) {
            int sourceStart = sourcePage * PageSize + sourcePageOffset;
            int destStart = destPage * PageSize + destPageOffset;
            int sourceEnd = sourceStart + length;
            int destEnd = destStart + length;

            if (sourceStart < destStart) {
                overlap = sourceEnd > destStart;
            } else {
                overlap = destEnd > sourceStart;
                reverse = overlap;
            }
        }

        if (!reverse) {
            int sourceOffset = sourcePageOffset;
            int currentSourcePage = sourcePage;
            int destOffset = destPageOffset;
            int currentDestPage = destPage;

            while (length > 0) {
                int size = Math.Min(Math.Min(length, PageSize - sourceOffset), PageSize - destOffset);
                Span<byte> source = GetLogicalPage(currentSourcePage);
                Span<byte> dest = GetLogicalPage(currentDestPage);
                if (source.IsEmpty || dest.IsEmpty) {
                    return 0x8A;
                }

                for (int i = 0; i < size; i++) {
                    dest[destOffset + i] = source[sourceOffset + i];
                }

                length -= size;
                sourceOffset += size;
                destOffset += size;

                if (sourceOffset == PageSize) {
                    sourceOffset = 0;
                    currentSourcePage++;
                }
                if (destOffset == PageSize) {
                    destOffset = 0;
                    currentDestPage++;
                }
            }
        } else {
            throw new NotImplementedException();
        }

        return overlap ? (byte)0x92 : (byte)0;
    }
}
