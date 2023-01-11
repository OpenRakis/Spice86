namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.Devices;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Provides DOS applications with EMS memory.
/// </summary>
public sealed class ExpandedMemoryManager : InterruptHandler, IDeviceCallbackProvider {
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

    private readonly short[] pageOwners = new short[MaximumLogicalPages];
    private readonly SortedList<int, EmsHandle> handles = new();
    private readonly int[] mappedPages = new int[MaximumPhysicalPages] { -1, -1, -1, -1 };
    private readonly uint xmsBaseAddress;

    public ExpandedMemoryManager(Machine machine) : base(machine) {
        this.pageOwners.AsSpan().Fill(-1);
        this._machine.Memory.SetString(0xF100, 0x000A, "EMMXXXX0");
        this._machine.Memory.Reserve(PageFrameSegment, PageSize * MaximumPhysicalPages);

        if (_machine.Xms?.TryAllocate(PageSize * MaximumLogicalPages, out short handle) != 0) {
            throw new InvalidOperationException("Could not allocate memory for EMS paging.");
        }

        if(_machine.Xms?.TryGetBlock(handle, out XmsBlock block) is true) {
            this.xmsBaseAddress = ExtendedMemoryManager.XmsBaseAddress + block.Offset;
        }

        for (int i = 0; i < 24; i++) {
            this.pageOwners[i] = SystemHandle;
        }

        this.handles[SystemHandle] = new EmsHandle(Enumerable.Range(0, 24).Select(i => (ushort)i));
    }

    /// <summary>
    /// Gets the total number of allocated EMS pages.
    /// </summary>
    public int AllocatedPages => this.handles.Values.Sum(p => p.PagesAllocated);

    public uint GetMappedAddress(uint fullAddress) {
        uint physicalPage = (fullAddress - (PageFrameSegment << 4)) / PageSize;
        int logicalPage = this.mappedPages[physicalPage];
        if (logicalPage != -1) {
            return (this.xmsBaseAddress + ((uint)logicalPage * PageSize)) | (fullAddress % PageSize);
        } else {
            return fullAddress;
        }
    }

    public override byte Index => 0x67;

    public bool IsHookable => throw new NotImplementedException();

    public SegmentedAddress CallbackAddress { set => throw new NotImplementedException(); }

    public override void Run() {
        switch (_machine.Cpu.State.AH) {
            case EmsFunctions.GetStatus:
                // Return good status in AH.
                _machine.Cpu.State.AH = 0;
                break;

            case EmsFunctions.GetPageFrameAddress:
                // Return page frame segment in BX.
                _machine.Cpu.State.BX = unchecked((ushort)PageFrameSegment);
                // Set good status.
                _machine.Cpu.State.AH = 0;
                break;

            case EmsFunctions.GetUnallocatedPageCount:
                // Return number of pages available in BX.
                _machine.Cpu.State.BX = (ushort)(MaximumLogicalPages - this.AllocatedPages);
                // Return total number of pages in DX.
                _machine.Cpu.State.DX = MaximumLogicalPages;
                // Set good status.
                _machine.Cpu.State.AH = 0;
                break;

            case EmsFunctions.AllocatePages:
                AllocatePages();
                break;

            case EmsFunctions.ReallocatePages:
                ReallocatePages();
                break;

            case EmsFunctions.MapUnmapHandlePage:
                MapUnmapHandlePage();
                break;

            case EmsFunctions.DeallocatePages:
                DeallocatePages();
                break;

            case EmsFunctions.GetVersion:
                // Return EMS version 4.0.
                _machine.Cpu.State.AL = 0x40;
                // Return good status.
                _machine.Cpu.State.AH = 0;
                break;

            case EmsFunctions.GetHandleCount:
                // Return the number of EMM handles (plus 1 for the OS handle).
                _machine.Cpu.State.BX = (ushort)(handles.Count + 1);
                // Return good status.
                _machine.Cpu.State.AH = 0;
                break;

            case EmsFunctions.GetHandlePages:
                GetHandlePages();
                break;

            case EmsFunctions.SavePageMap:
                SavePageMap();
                break;

            case EmsFunctions.RestorePageMap:
                RestorePageMap();
                break;

            case EmsFunctions.AdvancedMap:
                switch (_machine.Cpu.State.AL) {
                    case EmsFunctions.AdvancedMap_MapUnmapPages:
                        MapUnmapMultiplePages();
                        break;

                    default:
                        throw new InvalidOperationException();
                }
                break;

            case EmsFunctions.HandleName:
                switch (_machine.Cpu.State.AL) {
                    case EmsFunctions.HandleName_Get:
                        GetHandleName();
                        break;

                    case EmsFunctions.HandleName_Set:
                        SetHandleName();
                        break;

                    default:
                        throw new InvalidOperationException();
                }
                break;

            case EmsFunctions.GetHardwareInformation:
                switch (_machine.Cpu.State.AL) {
                    case EmsFunctions.GetHardwareInformation_UnallocatedRawPages:
                        // Return number of pages available in BX.
                        _machine.Cpu.State.BX = (ushort)(MaximumLogicalPages - this.AllocatedPages);
                        // Return total number of pages in DX.
                        _machine.Cpu.State.DX = MaximumLogicalPages;
                        // Set good status.
                        _machine.Cpu.State.AH = 0;
                        break;

                    default:
                        throw new InvalidOperationException();
                }
                break;

            case EmsFunctions.MoveExchange:
                switch (_machine.Cpu.State.AL) {
                    case EmsFunctions.MoveExchange_Move:
                        Move();
                        break;

                    default:
                        throw new NotImplementedException($"EMM function 57{this._machine.Cpu.State.AL:X2}h not implemented.");
                }
                break;

            case EmsFunctions.VCPI:
                System.Diagnostics.Debug.WriteLine($"VCPI function {_machine.Cpu.State.AL:X2}h not implemented.");
                break;

            default:
                System.Diagnostics.Debug.WriteLine($"EMM function {_machine.Cpu.State.AH:X2}h not implemented.");
                _machine.Cpu.State.AH = 0x84;
                break;
        }
    }

    /// <summary>
    /// Allocates pages for a new handle.
    /// </summary>
    private void AllocatePages() {
        uint pagesRequested = (ushort)_machine.Cpu.State.BX;
        if (pagesRequested == 0) {
            // Return "attempted to allocate zero pages" code.
            _machine.Cpu.State.AH = 0x89;
            return;
        }

        if (pagesRequested <= MaximumLogicalPages - this.AllocatedPages) {
            // Some programs like to use one more page than they ask for.
            // What a bunch of rubbish.
            int handle = CreateHandle((int)pagesRequested + 1);
            if (handle != 0) {
                // Return handle in DX.
                _machine.Cpu.State.DX = (ushort)handle;
                // Return good status.
                _machine.Cpu.State.AH = 0;
            } else {
                // Return "all handles in use" code.
                _machine.Cpu.State.AH = 0x85;
            }
        } else {
            // Return "not enough available pages" code.
            _machine.Cpu.State.AH = 0x87;
        }
    }
    /// <summary>
    /// Reallocates pages for a handle.
    /// </summary>
    private void ReallocatePages() {
        int pagesRequested = _machine.Cpu.State.BX;

        if (pagesRequested < MaximumLogicalPages) {
            int handle = _machine.Cpu.State.DX;
            if (handles.TryGetValue(handle, out EmsHandle? emsHandle)) {
                if (pagesRequested < emsHandle.PagesAllocated) {
                    for (int i = emsHandle.LogicalPages.Count - 1; i >= emsHandle.LogicalPages.Count - pagesRequested; i--) {
                        this.mappedPages[emsHandle.LogicalPages[i]] = -1;
                    }

                    emsHandle.LogicalPages.RemoveRange(emsHandle.LogicalPages.Count - pagesRequested, emsHandle.PagesAllocated - pagesRequested);
                } else if (pagesRequested > emsHandle.PagesAllocated) {
                    int pagesToAdd = pagesRequested - emsHandle.PagesAllocated;
                    for (int i = 0; i < pagesToAdd; i++) {
                        ushort logicalPage = this.GetNextFreePage((short)handle);
                        emsHandle.LogicalPages.Add(logicalPage);
                    }
                }

                // Return good status.
                _machine.Cpu.State.AH = 0;
            } else {
                // Return "couldn't find specified handle" code.
                _machine.Cpu.State.AH = 0x83;
            }
        } else {
            // Return "not enough available pages" code.
            _machine.Cpu.State.AH = 0x87;
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
    private void DeallocatePages() {
        int handle = _machine.Cpu.State.DX;
        if (handles.Remove(handle)) {
            for (int i = 0; i < this.pageOwners.Length; i++) {
                if (this.pageOwners[i] == handle) {
                    this.pageOwners[i] = -1;
                }
            }

            // Return good status.
            _machine.Cpu.State.AH = 0;
        } else {
            // Return "couldn't find specified handle" code.
            _machine.Cpu.State.AH = 0x83;
        }
    }
    /// <summary>
    /// Maps or unmaps a physical page.
    /// </summary>
    private void MapUnmapHandlePage() {
        int physicalPage = _machine.Cpu.State.AL;
        if (physicalPage < 0 || physicalPage >= MaximumPhysicalPages) {
            // Return "physical page out of range" code.
            _machine.Cpu.State.AH = 0x8B;
            return;
        }

        int handleIndex = _machine.Cpu.State.DX;
        if (!handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return "couldn't find specified handle" code.
            _machine.Cpu.State.AH = 0x83;
            return;
        }

        int logicalPageIndex = (ushort)_machine.Cpu.State.BX;

        if (logicalPageIndex != 0xFFFF) {
            if (logicalPageIndex < 0 || logicalPageIndex >= handle.LogicalPages.Count) {
                // Return "logical page out of range" code.
                _machine.Cpu.State.AH = 0x8A;
                return;
            }

            this.MapPage(handle.LogicalPages[logicalPageIndex], physicalPage);
        } else {
            this.UnmapPage(physicalPage);
        }

        // Return good status.
        _machine.Cpu.State.AH = 0;
    }
    /// <summary>
    /// Copies data from a logical page to a physical page.
    /// </summary>
    /// <param name="logicalPage">Logical page to copy from.</param>
    /// <param name="physicalPageIndex">Index of physical page to copy to.</param>
    private void MapPage(int logicalPage, int physicalPageIndex) {
        // If the requested logical page is already mapped, it needs to get unmapped first.
        this.UnmapLogicalPage(logicalPage);

        // If a page is already mapped, make sure it gets unmapped first.
        this.UnmapPage(physicalPageIndex);

        //var pageFrame = this.GetMappedPage(physicalPageIndex);
        //var xms = this.GetLogicalPage(logicalPage);
        //xms.CopyTo(pageFrame);
        this.mappedPages[physicalPageIndex] = logicalPage;
    }
    /// <summary>
    /// Copies data from a physical page to a logical page.
    /// </summary>
    /// <param name="physicalPageIndex">Physical page to copy from.</param>
    private void UnmapPage(int physicalPageIndex) {
        int currentPage = this.mappedPages[physicalPageIndex];
        if (currentPage != -1) {
            //var pageFrame = this.GetMappedPage(physicalPageIndex);
            //var xms = this.GetLogicalPage(currentPage);
            //pageFrame.CopyTo(xms);
            this.mappedPages[physicalPageIndex] = -1;
        }
    }
    /// <summary>
    /// Unmaps a specific logical page if it is currently mapped.
    /// </summary>
    /// <param name="logicalPage">Logical page to unmap.</param>
    private void UnmapLogicalPage(int logicalPage) {
        for (int i = 0; i < this.mappedPages.Length; i++) {
            if (this.mappedPages[i] == logicalPage) {
                this.UnmapPage(i);
            }
        }
    }
    /// <summary>
    /// Gets the number of pages allocated to a handle.
    /// </summary>
    private void GetHandlePages() {
        int handleIndex = _machine.Cpu.State.DX;
        if (handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return the number of pages allocated in BX.
            _machine.Cpu.State.BX = (ushort)handle.PagesAllocated;
            // Return good status.
            _machine.Cpu.State.AH = 0;
        } else {
            // Return "couldn't find specified handle" code.
            _machine.Cpu.State.AH = 0x83;
        }
    }
    /// <summary>
    /// Gets the name of a handle.
    /// </summary>
    private void GetHandleName() {
        int handleIndex = _machine.Cpu.State.DX;
        if (handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Write the handle name to ES:DI.
            _machine.Memory.SetString(_machine.Cpu.State.ES, _machine.Cpu.State.DI, handle.Name);
            // Return good status.
            _machine.Cpu.State.AH = 0;
        } else {
            // Return "couldn't find specified handle" code.
            _machine.Cpu.State.AH = 0x83;
        }
    }
    /// <summary>
    /// Set the name of a handle.
    /// </summary>
    private void SetHandleName() {
        int handleIndex = _machine.Cpu.State.DX;
        if (handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Read the handle name from DS:SI.
            handle.Name = _machine.Memory.GetString(_machine.Cpu.State.DS, _machine.Cpu.State.SI, 8);
            // Return good status.
            _machine.Cpu.State.AH = 0;
        } else {
            // Return "couldn't find specified handle" code.
            _machine.Cpu.State.AH = 0x83;
        }
    }

    /// <summary>
    /// Maps or unmaps multiple pages.
    /// </summary>
    private void MapUnmapMultiplePages() {
        int handleIndex = _machine.Cpu.State.DX;
        if (!handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return "couldn't find specified handle" code.
            _machine.Cpu.State.AH = 0x83;
            return;
        }

        int pageCount = _machine.Cpu.State.CX;
        if (pageCount < 0 || pageCount > MaximumPhysicalPages) {
            // Return "physical page count out of range" code.
            _machine.Cpu.State.AH = 0x8B;
            return;
        }

        uint arraySegment = _machine.Cpu.State.DS;
        uint arrayOffset = _machine.Cpu.State.SI;
        for (int i = 0; i < pageCount; i++) {
            ushort logicalPageIndex = _machine.Memory.GetUint16(arraySegment, arrayOffset);
            ushort physicalPageIndex = _machine.Memory.GetUint16(arraySegment, arrayOffset + 2u);

            if (physicalPageIndex < 0 || physicalPageIndex >= MaximumPhysicalPages) {
                // Return "physical page out of range" code.
                _machine.Cpu.State.AH = 0x8B;
                return;
            }

            if (logicalPageIndex != 0xFFFF) {
                if (logicalPageIndex < 0 || logicalPageIndex >= handle.LogicalPages.Count) {
                    // Return "logical page out of range" code.
                    _machine.Cpu.State.AH = 0x8A;
                    return;
                }

                this.MapPage(handle.LogicalPages[logicalPageIndex], physicalPageIndex);
            } else {
                this.UnmapPage(physicalPageIndex);
            }

            arrayOffset += 4u;
        }

        // Return good status.
        _machine.Cpu.State.AH = 0;
    }
    /// <summary>
    /// Saves the current state of page map registers for a handle.
    /// </summary>
    private void SavePageMap() {
        int handleIndex = _machine.Cpu.State.DX;
        if (!handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return "couldn't find specified handle" code.
            _machine.Cpu.State.AH = 0x83;
            return;
        }

        this.mappedPages.CopyTo(handle.SavedPageMap);

        // Return good status.
        _machine.Cpu.State.AH = 0;
    }
    /// <summary>
    /// Restores the state of page map registers for a handle.
    /// </summary>
    private void RestorePageMap() {
        int handleIndex = _machine.Cpu.State.DX;
        if (!handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return "couldn't find specified handle" code.
            _machine.Cpu.State.AH = 0x83;
            return;
        }

        if (handle.SavedPageMap != null) {
            for (int i = 0; i < MaximumPhysicalPages; i++) {
                if (handle.SavedPageMap[i] != mappedPages[i]) {
                    this.MapPage(handle.SavedPageMap[i], i);
                }
            }
        }

        // Return good status.
        _machine.Cpu.State.AH = 0;
    }
    /// <summary>
    /// Copies a block of memory.
    /// </summary>
    private void Move() {
#warning make sure works with new paging system

        int length = (int)_machine.Memory.GetUint32(_machine.Cpu.State.DS, _machine.Cpu.State.SI);

        byte sourceType = _machine.Memory.GetByte(_machine.Cpu.State.DS, _machine.Cpu.State.SI + 4u);
        int sourceHandleIndex = _machine.Memory.GetUint16(_machine.Cpu.State.DS, _machine.Cpu.State.SI + 5u);
        int sourceOffset = _machine.Memory.GetUint16(_machine.Cpu.State.DS, _machine.Cpu.State.SI + 7u);
        int sourcePage = _machine.Memory.GetUint16(_machine.Cpu.State.DS, _machine.Cpu.State.SI + 9u);

        byte destType = _machine.Memory.GetByte(_machine.Cpu.State.DS, _machine.Cpu.State.SI + 11u);
        int destHandleIndex = _machine.Memory.GetUint16(_machine.Cpu.State.DS, _machine.Cpu.State.SI + 12u);
        int destOffset = _machine.Memory.GetUint16(_machine.Cpu.State.DS, _machine.Cpu.State.SI + 14u);
        int destPage = _machine.Memory.GetUint16(_machine.Cpu.State.DS, _machine.Cpu.State.SI + 16u);

        this.SyncToEms();

        if (sourceType == 0 && destType == 0) {
            _machine.Cpu.State.AH = this.ConvToConv((uint)((sourcePage << 4) + sourceOffset), (uint)((destPage << 4) + destOffset), length);
        } else if (sourceType != 0 && destType == 0) {
            if (!handles.TryGetValue(sourceHandleIndex, out _)) {
                // Return "couldn't find specified handle" code.
                _machine.Cpu.State.AH = 0x83;
                return;
            }

            _machine.Cpu.State.AH = this.EmsToConv(sourcePage, sourceOffset, (uint)((destPage << 4) + destOffset), length);
        } else if (sourceType == 0 && destType != 0) {
            if (!handles.TryGetValue(destHandleIndex, out _)) {
                // Return "couldn't find specified handle" code.
                _machine.Cpu.State.AH = 0x83;
                return;
            }

            _machine.Cpu.State.AH = this.ConvToEms((uint)((sourcePage << 4) + sourceOffset), destPage, destOffset, length);
        } else {
            if (!handles.TryGetValue(sourceHandleIndex, out EmsHandle? sourceHandle) || !handles.TryGetValue(destHandleIndex, out EmsHandle? destHandle)) {
                // Return "couldn't find specified handle" code.
                _machine.Cpu.State.AH = 0x83;
                return;
            }

            _machine.Cpu.State.AH = this.EmsToEms(sourceHandle, sourcePage, sourceOffset, destHandle, destPage, destOffset, length);
        }

        this.SyncFromEms();
    }
    /// <summary>
    /// Copies data from mapped conventional memory to EMS pages.
    /// </summary>
    private void SyncToEms() {
        for (int i = 0; i < MaximumPhysicalPages; i++) {
            if (this.mappedPages[i] != -1) {
                Span<byte> src = this.GetMappedPage(i);
                Span<byte> dest = this.GetLogicalPage(this.mappedPages[i]);
                src.CopyTo(dest);
            }
        }
    }
    /// <summary>
    /// Copies data from EMS pages to mapped conventional memory.
    /// </summary>
    private void SyncFromEms() {
        for (int i = 0; i < MaximumPhysicalPages; i++) {
            if (this.mappedPages[i] != -1) {
                Span<byte> src = this.GetLogicalPage(this.mappedPages[i]);
                Span<byte> dest = this.GetMappedPage(i);
                src.CopyTo(dest);
            }
        }
    }

    private Span<byte> GetMappedPage(int physicalPageIndex) => this._machine.Memory.Span.Slice((PageFrameSegment << 4) + (physicalPageIndex * PageSize), PageSize);
    private Span<byte> GetLogicalPage(int logicalPageIndex) => this._machine.Memory.Span.Slice((int)this.xmsBaseAddress + (logicalPageIndex * PageSize), PageSize);
    private ushort GetNextFreePage(short handle) {
        for (int i = 0; i < this.pageOwners.Length; i++) {
            if (this.pageOwners[i] == -1) {
                this.pageOwners[i] = handle;
                return (ushort)i;
            }
        }

        return 0;
    }

    private byte ConvToConv(uint sourceAddress, uint destAddress, int length) {
        if (length < 0) {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length == 0) {
            return 0;
        }

        if (sourceAddress + length > Memory.ConvMemorySize || destAddress + length > Memory.ConvMemorySize) {
            return 0xA2;
        }

        bool overlap = (sourceAddress + length - 1 >= destAddress || destAddress + length - 1 >= sourceAddress);
        bool reverse = overlap && sourceAddress > destAddress;
        Memory memory = this._machine.Memory;

        if (!reverse) {
            for (uint offset = 0; offset < length; offset++) {
                memory.SetByte(destAddress + offset, memory.GetByte(sourceAddress + offset));
            }
        } else {
            for (int offset = length - 1; offset >= 0; offset--) {
                memory.SetByte(destAddress + (uint)offset, memory.GetByte(sourceAddress + (uint)offset));
            }
        }

        return overlap ? (byte)0x92 : (byte)0;
    }
    private byte EmsToConv(int sourcePage, int sourcePageOffset, uint destAddress, int length) {
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
        Memory memory = this._machine.Memory;
        while (length > 0) {
            int size = Math.Min(length, PageSize - offset);
            Span<byte> source = this.GetLogicalPage(pageIndex);
            if (source.IsEmpty) {
                return 0x8A;
            }

            for (int i = 0; i < size; i++) {
                memory.SetByte(sourceCount++, source[offset + i]);
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

        Memory memory = this._machine.Memory;
        int offset = destPageOffset;
        uint sourceCount = sourceAddress;
        int pageIndex = destPage;
        while (length > 0) {
            int size = Math.Min(length, PageSize - offset);
            Span<byte> target = this.GetLogicalPage(pageIndex);
            if (target.IsEmpty) {
                return 0x8A;
            }

            for (int i = 0; i < size; i++) {
                target[offset + i] = memory.GetByte(sourceCount++);
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

        if (sourcePageOffset >= ExpandedMemoryManager.PageSize || destPageOffset >= ExpandedMemoryManager.PageSize) {
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
                int size = Math.Min(Math.Min(length, ExpandedMemoryManager.PageSize - sourceOffset), ExpandedMemoryManager.PageSize - destOffset);
                Span<byte> source = this.GetLogicalPage(currentSourcePage);
                Span<byte> dest = this.GetLogicalPage(currentDestPage);
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

    public void InvokeCallback() {
        throw new NotImplementedException();
    }
}
