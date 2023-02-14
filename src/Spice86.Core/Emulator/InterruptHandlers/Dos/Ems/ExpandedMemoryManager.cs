using Serilog;
using Serilog.Events;

using System.Numerics;

namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

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
    public const int FirstHandle = 1;
    public const int LastHandle = 254;
    public const int SystemHandle = 0;

    public const string EmsIdentifier = "EMMXXXX0";

    public Memory ExpandedMemory { get; init; }

    private readonly short[] _pageOwners = new short[MaximumLogicalPages];
    private readonly SortedList<int, EmsHandle> _handles = new();
    private readonly int[] _mappedPages = new int[] {-1, -1, -1, -1};

    private readonly ILoggerService _loggerService;
    
    public ExpandedMemoryManager(Machine machine, ILoggerService loggerService) : base(machine) {
        _loggerService = loggerService;
        ExpandedMemory = new(machine,8 * 1024);
        MemoryUtils.SetZeroTerminatedString(ExpandedMemory.Ram, MemoryUtils.ToPhysicalAddress(0xF100 - PageFrameSegment, 0x000A), EmsIdentifier, EmsIdentifier.Length + 1);

        _pageOwners.AsSpan().Fill(-1);

        for (int i = 0; i < 24; i++) {
            _pageOwners[i] = SystemHandle;
        }

        _handles[SystemHandle] = new EmsHandle(Enumerable.Range(0, 24).Select(i => (ushort)i));
        FillDispatchTable();
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x40, new Callback(0x40, GetStatus));
        _dispatchTable.Add(0x41, new Callback(0x41, GetPageFrameSegment));
        _dispatchTable.Add(0x42, new Callback(0x42, GetNumberOfPages));
        _dispatchTable.Add(0x43, new Callback(0x43, GetHandleAndAllocatePages));
        _dispatchTable.Add(0x44, new Callback(0x44, MapExpandedMeoryPage));
        _dispatchTable.Add(0x45, new Callback(0x45, ReleaseHandleAndFreePages));
        _dispatchTable.Add(0x46, new Callback(0x46, GetEmmVersion));
        _dispatchTable.Add(0x47, new Callback(0x47, SavePageMap));
        _dispatchTable.Add(0x48, new Callback(0x48, RestorePageMap));
        _dispatchTable.Add(0x4B, new Callback(0x4B, GetHandleCount));
        _dispatchTable.Add(0x4C, new Callback(0x4C, GetPagesForOneHandle));
        _dispatchTable.Add(0x4D, new Callback(0x4D, GetPagesForAllHandles));
        _dispatchTable.Add(0x4E, new Callback(0x4E, SaveRestorePageMap));
        _dispatchTable.Add(0x4F, new Callback(0x4F, SaveRestorePartialPageMap));
        _dispatchTable.Add(0x50, new Callback(0x50, MapOrUnmapMultiplePageMap));
        _dispatchTable.Add(0x51, new Callback(0x51, ReallocatePages));
        _dispatchTable.Add(0x53, new Callback(0x53, SetGetHandleName));
        _dispatchTable.Add(0x54, new Callback(0x54, HandleFunctions));
        _dispatchTable.Add(0x57, new Callback(0x57, MemoryRegion));
        _dispatchTable.Add(0x58, new Callback(0x58, GetMappablePhysicalArrayAddressArray));
        _dispatchTable.Add(0x5A, new Callback(0x5A, AllocateStandardRawPages));
        _dispatchTable.Add(0x59, new Callback(0x59, GetHardwareInformation));
    }

    public void AllocateStandardRawPages() {
        if (_state.AX <= 0x01) {
            ushort dx = _state.DX;
            _state.AX = EmsAllocateMemory(_state.BX, ref dx, true);
            _state.DX = dx;
        } else {
            if (_loggerService.IsEnabled(LogEventLevel.Error))
                _loggerService.Error("EMS:Call 5A {@SubFunction} not supported", _state.AX);
            _state.AX = EmsErrors.EmsInvalidSubFunction;
        }
    }

    public byte EmsAllocateMemory(ushort pages, ref ushort dx, bool canAllocateZeroPages) {
        // Check for 0 page allocation
        if (pages == 0) {
            if (!canAllocateZeroPages) return EmsErrors.EmmZeroPages;
        }
        // Check for a free handle
        // Change handle only if there is no error.
        if (TryCreateHandle(pages, out int handleIndex)) {
            dx = (ushort)handleIndex;
        }

        return EmsErrors.EmmNoError;
    }

    public void GetMappablePhysicalArrayAddressArray() {
    }

    public void HandleFunctions() {
    }

    public void SaveRestorePartialPageMap() {
    }

    public void SaveRestorePageMap() {
    }

    public void GetPagesForAllHandles() {
    }

    public bool TryGetMappedPageData(uint address, out uint data) {
        if (address is < (PageFrameSegment << 4) or >= (PageFrameSegment << 4) + 65536) {
            data = 0;
            return false;
        }
        data = ExpandedMemory.GetUint32(address);
        return true;
    }

    public bool TryGetMappedPageData(uint address, out ushort data) {
        if (address is < (PageFrameSegment << 4) or >= (PageFrameSegment << 4) + 65536) {
            data = 0;
            return false;
        }
        data = ExpandedMemory.GetUint16(address);
        return true;
    }
    
    public bool TryGetMappedPageData(uint address, out byte data) {
        if (address is < (PageFrameSegment << 4) or >= (PageFrameSegment << 4) + 65536) {
            data = 0;
            return false;
        }
        data = ExpandedMemory.GetUint8(address);
        return true;
    }

    public bool TryWriteMappedPageData<T>(uint address, T data) where T : INumber<T> {
        if (address is < (PageFrameSegment << 4) or >= (PageFrameSegment << 4) + 65536) {
            return false;
        }
        switch (data)
        {
            case byte b:
                ExpandedMemory.SetUint8(address, b);
                break;
            case ushort u:
                ExpandedMemory.SetUint16(address, u);
                break;
            case uint i:
                ExpandedMemory.SetUint32(address, i);
                break;
        }
        return true;
    }

    public void MapOrUnmapMultiplePageMap() {
        switch (_state.AL) {
            case EmsSubFunctions.MapUnmapPages:
                MapUnmapMultiplePages();
                break;
            default:
                throw new UnrecoverableException("Not implemented EMS subfunction", new NotImplementedException($"{_state.AL} function not implemented"));
        }
    }

    public void GetPageFrameSegment() {
        // Return page frame segment in BX.
        _state.BX = PageFrameSegment;
        // Set good status.
        _state.AH = EmsErrors.EmmNoError;
    }

    public void GetNumberOfPages() {
        // Return number of pages available in BX.
        _state.BX = (ushort)(MaximumLogicalPages - AllocatedPages);
        // Return total number of pages in DX.
        _state.DX = MaximumLogicalPages;
        // Set good status.
        _state.AH = EmsErrors.EmmNoError;
    }

    public void GetEmmVersion() {
        // Return EMS version 4.0.
        _state.AL = 0x40;
        // Return good status.
        _state.AH = 0;
    }

    public void GetHardwareInformation() {
        switch (_state.AL) {
        case EmsSubFunctions.GetHardwareInformationUnallocatedRawPages:
            // Return number of pages available in BX.
            _state.BX = (ushort)(MaximumLogicalPages - AllocatedPages);
            // Return total number of pages in DX.
            _state.DX = MaximumLogicalPages;
            // Set good status.
            _state.AH = EmsErrors.EmmNoError;
            break;

        default:
            throw new UnrecoverableException();
        }
    }

    public void GetHandleCount() {
        // Return the number of EMM handles (plus 1 for the OS handle).
        _state.BX = (ushort)(_handles.Count + 1);
        // Return good status.
        _state.AH = EmsErrors.EmmNoError;
    }

    public void AdvanceMap() {
        switch (_state.AL) {
            case EmsSubFunctions.MapUnmapPages:
                MapUnmapMultiplePages();
                break;
            default:
                throw new UnrecoverableException();
        }
    }

    public void SetGetHandleName() {
        switch (_state.AL) {
            case EmsSubFunctions.HandleNameGet:
                GetHandleName();
                break;

            case EmsSubFunctions.HandleNameSet:
                SetHandleName();
                break;

            default:
                throw new UnrecoverableException();
        }
    }

    public void MemoryRegion() {
        switch (_state.AL) {
            case EmsSubFunctions.MoveExchangeMove:
                Move();
                break;

            default:
                throw new NotImplementedException($"EMM function 57{_state.AL:X2}h not implemented.");
        }
    }

    /// <summary>
    /// Gets the total number of allocated EMS pages.
    /// </summary>
    public int AllocatedPages => _handles.Values.Sum(p => p.PagesAllocated);

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
    public void GetHandleAndAllocatePages() {
        uint pagesRequested = _state.BX;
        if (pagesRequested == 0) {
            // Return "attempted to allocate zero pages" code.
            _state.AH = EmsErrors.EmmZeroPages;
            return;
        }

        if (pagesRequested <= MaximumLogicalPages - AllocatedPages) {
            // Some programs like to use one more page than they ask for.
            // What a bunch of rubbish.
            
            if (TryCreateHandle((int)pagesRequested + 1, out int handle)) {
                // Return handle in DX.
                _state.DX = (ushort)handle;
                // Return good status.
                _state.AH = EmsErrors.EmmNoError;
            } else {
                // Return "all handles in use" code.
                _state.AH = EmsErrors.EmmOutOfHandles;
            }
        } else {
            // Return "not enough available pages" code.
            _state.AH = EmsErrors.EmmOutOfPhysicalPages;
        }
    }

    /// <summary>
    /// Reallocates pages for a handle.
    /// </summary>
    public void ReallocatePages() {
        int pagesRequested = _state.BX;

        if (pagesRequested < MaximumLogicalPages) {
            int handle = _state.DX;
            if (_handles.TryGetValue(handle, out EmsHandle? emsHandle)) {
                if (pagesRequested < emsHandle.PagesAllocated) {
                    for (int i = emsHandle.LogicalPages.Count - 1; i >= emsHandle.LogicalPages.Count - pagesRequested; i--) {
                        _mappedPages[emsHandle.LogicalPages[i]] = -1;
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
                _state.AH = EmsErrors.EmmNoError;
            } else {
                // Return "couldn't find specified handle" code.
                _state.AH = EmsErrors.EmsIllegalPhysicalPage;
            }
        } else {
            // Return "not enough available pages" code.
            _state.AH = EmsErrors.EmmOutOfPhysicalPages;
        }
    }

    /// <summary>
    /// Attempts to create a new EMS handle. Returns <c>false</c> if no handle could be created.
    /// </summary>
    /// <param name="pagesRequested">Number of pages to allocate to the new handle.</param>
    /// <param name="handleIndex">Index for the newly created handle, if returned status is <c>true</c>.</param>

    /// <returns>New EMS handle if created successfully; otherwise zero.</returns>
    public bool TryCreateHandle(int pagesRequested, out int handleIndex) {
        for (int i = FirstHandle; i <= LastHandle; i++) {
            if (!_handles.ContainsKey(i)) {
                var pages = new List<ushort>(pagesRequested);
                for (int p = 0; p < pagesRequested; p++) {
                    pages.Add(GetNextFreePage((short)i));
                }
                var handle = new EmsHandle(pages);
                _handles.Add(i, handle);
                handleIndex = i;
                return true;
            }
        }

        handleIndex = 0;
        return false;
    }

    /// <summary>
    /// Deallocates a handle and all of its pages.
    /// </summary>
    public void ReleaseHandleAndFreePages() {
        int handle = _state.DX;
        if (_handles.Remove(handle)) {
            for (int i = 0; i < _pageOwners.Length; i++) {
                if (_pageOwners[i] == handle) {
                    _pageOwners[i] = -1;
                }
            }

            // Return good status.
            _state.AH = EmsErrors.EmmNoError;
        } else {
            // Return "couldn't find specified handle" code.
            _state.AH = EmsErrors.EmmInvalidHandle;
        }
    }

    /// <summary>
    /// Maps or unmaps a physical page.
    /// </summary>
    public void MapExpandedMeoryPage() {
        int physicalPage = _state.AL;
        if (physicalPage is < 0 or >= MaximumPhysicalPages) {
            // Return "physical page out of range" code.
            _state.AH = EmsErrors.EmsIllegalPhysicalPage;
            return;
        }

        int handleIndex = _state.DX;
        if (!_handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return "couldn't find specified handle" code.
            _state.AH = EmsErrors.EmmInvalidHandle;
            return;
        }

        int logicalPageIndex = _state.BX;

        if (logicalPageIndex != 0xFFFF) {
            if (logicalPageIndex >= handle.LogicalPages.Count) {
                // Return "logical page out of range" code.
                _state.AH = EmsErrors.EmsLogicalPageOutOfRange;
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
    public void MapPage(int logicalPage, int physicalPageIndex) {
        // If the requested logical page is already mapped, it needs to get unmapped first.
        UnmapLogicalPage(logicalPage);

        // If a page is already mapped, make sure it gets unmapped first.
        UnmapPage(physicalPageIndex);

        Span<byte> pageFrame = this.GetMappedPage(physicalPageIndex);
        Span<byte> ems = this.GetLogicalPage(logicalPage);
        ems.CopyTo(pageFrame);
        _mappedPages[physicalPageIndex] = logicalPage;
    }

    /// <summary>
    /// Copies data from a physical page to a logical page.
    /// </summary>
    /// <param name="physicalPageIndex">Physical page to copy from.</param>
    public void UnmapPage(int physicalPageIndex) {
        int currentPage = _mappedPages[physicalPageIndex];
        if (currentPage != -1) {
            Span<byte> pageFrame = GetMappedPage(physicalPageIndex);
            Span<byte> ems = GetLogicalPage(currentPage);
            pageFrame.CopyTo(ems);
            _mappedPages[physicalPageIndex] = -1;
        }
    }

    /// <summary>
    /// Unmaps a specific logical page if it is currently mapped.
    /// </summary>
    /// <param name="logicalPage">Logical page to unmap.</param>
    public void UnmapLogicalPage(int logicalPage) {
        for (int i = 0; i < _mappedPages.Length; i++) {
            if (_mappedPages[i] == logicalPage) {
                UnmapPage(i);
            }
        }
    }

    /// <summary>
    /// Gets the number of pages allocated to a handle.
    /// </summary>
    public void GetPagesForOneHandle() {
        int handleIndex = _state.DX;
        if (_handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return the number of pages allocated in BX.
            _state.BX = (ushort)handle.PagesAllocated;
            // Return good status.
            _state.AH = EmsErrors.EmmNoError;
        } else {
            // Return "couldn't find specified handle" code.
            _state.AH = EmsErrors.EmmInvalidHandle;
        }
    }

    /// <summary>
    /// Gets the name of a handle.
    /// </summary>
    public void GetHandleName() {
        int handleIndex = _state.DX;
        if (_handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Write the handle name to ES:DI.
            MemoryUtils.SetZeroTerminatedString(this.ExpandedMemory.Ram, MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI), handle.Name, handle.Name.Length + 1);
            // Return good status.
            _state.AH = EmsErrors.EmmNoError;
        } else {
            // Return "couldn't find specified handle" code.
            _state.AH = EmsErrors.EmmInvalidHandle;
        }
    }

    /// <summary>
    /// Set the name of a handle.
    /// </summary>
    public void SetHandleName() {
        int handleIndex = _state.DX;
        if (_handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Read the handle name from DS:SI.
            handle.Name = MemoryUtils.GetZeroTerminatedString(ExpandedMemory.Ram, MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI), 8);
            // Return good status.
            _state.AH = EmsErrors.EmmNoError;
        } else {
            // Return "couldn't find specified handle" code.
            _state.AH = EmsErrors.EmmInvalidHandle;
        }
    }

    /// <summary>
    /// Maps or unmaps multiple pages.
    /// </summary>
    public void MapUnmapMultiplePages() {
        int handleIndex = _state.DX;
        if (!_handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return "couldn't find specified handle" code.
            _state.AH = EmsErrors.EmmInvalidHandle;
            return;
        }

        int pageCount = _state.CX;
        if (pageCount is < 0 or > MaximumPhysicalPages) {
            // Return "physical page count out of range" code.
            _state.AH = EmsErrors.EmsIllegalPhysicalPage;
            return;
        }

        ushort arraySegment = _state.DS;
        ushort arrayOffset = _state.SI;
        for (int i = 0; i < pageCount; i++) {
            ushort logicalPageIndex = ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(arraySegment, arrayOffset));
            ushort physicalPageIndex = ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(arraySegment, (ushort)(arrayOffset + 2u)));

            if (physicalPageIndex >= MaximumPhysicalPages) {
                // Return "physical page out of range" code.
                _state.AH = EmsErrors.EmsIllegalPhysicalPage;
                return;
            }

            if (logicalPageIndex != 0xFFFF) {
                if (logicalPageIndex >= handle.LogicalPages.Count) {
                    // Return "logical page out of range" code.
                    _state.AH = EmsErrors.EmsLogicalPageOutOfRange;
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
        if (!_handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return "couldn't find specified handle" code.
            _state.AH = EmsErrors.EmmInvalidHandle;
            return;
        }

        _mappedPages.CopyTo(handle.SavedPageMap);

        // Return good status.
        _state.AH = EmsErrors.EmmNoError;
    }
    /// <summary>
    /// Restores the state of page map registers for a handle.
    /// </summary>
    public void RestorePageMap() {
        int handleIndex = _state.DX;
        if (!_handles.TryGetValue(handleIndex, out EmsHandle? handle)) {
            // Return "couldn't find specified handle" code.
            _state.AH = EmsErrors.EmmInvalidHandle;
            return;
        }

        for (int i = 0; i < MaximumPhysicalPages; i++) {
            if (handle.SavedPageMap[i] != _mappedPages[i]) {
                MapPage(handle.SavedPageMap[i], i);
            }
        }

        // Return good status.
        _state.AH = EmsErrors.EmmNoError;
    }
    /// <summary>
    /// Copies a block of memory.
    /// </summary>
    public void Move() {
        int length = (int)ExpandedMemory.GetUint32(MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI));

        byte sourceType = ExpandedMemory.GetUint8(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 4u)));
        int sourceHandleIndex = ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 5u)));
        int sourceOffset = ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 7u)));
        int sourcePage = ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 9u)));

        byte destType = ExpandedMemory.GetUint8(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 11u)));
        int destHandleIndex = ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 12u)));
        int destOffset = ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 14u)));
        int destPage = ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 16u)));

        CopyDataFromMappedConvMemoryToEmsPages();

        if (sourceType == 0 && destType == 0) {
            _state.AH = CopyConventionalMemoryToConventionalMemory((uint)((sourcePage << 4) + sourceOffset), (uint)((destPage << 4) + destOffset), length);
        } else if (sourceType != 0 && destType == 0) {
            if (!_handles.TryGetValue(sourceHandleIndex, out _)) {
                // Return "couldn't find specified handle" code.
                _state.AH = EmsErrors.EmmInvalidHandle;
                return;
            }

            _state.AH = CopyEmsToConventionalMemory(sourcePage, sourceOffset, (uint)((destPage << 4) + destOffset), length);
        } else if (sourceType == 0 && destType != 0) {
            if (!_handles.TryGetValue(destHandleIndex, out _)) {
                // Return "couldn't find specified handle" code.
                _state.AH = 0x83;
                return;
            }

            _state.AH = CopyConventionalMemoryToEmsMemory((uint)((sourcePage << 4) + sourceOffset), destPage, destOffset, length);
        } else {
            if (!_handles.TryGetValue(sourceHandleIndex, out EmsHandle? sourceHandle) || !_handles.TryGetValue(destHandleIndex, out EmsHandle? destHandle)) {
                // Return "couldn't find specified handle" code.
                _state.AH = EmsErrors.EmmInvalidHandle;
                return;
            }

            _state.AH = CopyEmsToEms(sourceHandle, sourcePage, sourceOffset, destHandle, destPage, destOffset, length);
        }

        CopyDataFromEmsPagesToMappedConvMem();
    }
    
    public void CopyDataFromMappedConvMemoryToEmsPages() {
        for (int i = 0; i < MaximumPhysicalPages; i++) {
            if (_mappedPages[i] != -1) {
                Span<byte> src = GetMappedPage(i);
                Span<byte> dest = GetLogicalPage(_mappedPages[i]);
                src.CopyTo(dest);
            }
        }
    }
    
    public void CopyDataFromEmsPagesToMappedConvMem() {
        for (int i = 0; i < MaximumPhysicalPages; i++) {
            if (_mappedPages[i] != -1) {
                Span<byte> src = GetLogicalPage(_mappedPages[i]);
                Span<byte> dest = GetMappedPage(i);
                src.CopyTo(dest);
            }
        }
    }

    public Span<byte> GetMappedPage(int physicalPageIndex) => ExpandedMemory.GetSpan(0, ExpandedMemory.Ram.Length).Slice((PageFrameSegment << 4) + physicalPageIndex * PageSize, PageSize);

    public Span<byte> GetLogicalPage(int logicalPageIndex) => ExpandedMemory.GetSpan(0, ExpandedMemory.Ram.Length).Slice(logicalPageIndex * PageSize, PageSize);
    
    public ushort GetNextFreePage(short handle) {
        for (int i = 0; i < _pageOwners.Length; i++) {
            if (_pageOwners[i] == -1) {
                _pageOwners[i] = handle;
                return (ushort)i;
            }
        }

        return 0;
    }

    public byte CopyConventionalMemoryToConventionalMemory(uint sourceAddress, uint destAddress, int length) {
        switch (length)
        {
            case < 0:
                throw new ArgumentOutOfRangeException(nameof(length));
            case 0:
                return 0;
        }

        if (sourceAddress + length > MainMemory.ConvMemorySize || destAddress + length > MainMemory.ConvMemorySize) {
            return 0xA2;
        }

        bool overlap = sourceAddress + length - 1 >= destAddress || destAddress + length - 1 >= sourceAddress;
        bool reverse = overlap && sourceAddress > destAddress;

        if (!reverse) {
            for (uint offset = 0; offset < length; offset++) {
                ExpandedMemory.SetUint8(destAddress + offset, ExpandedMemory.GetUint8(sourceAddress + offset));
            }
        } else {
            for (int offset = length - 1; offset >= 0; offset--) {
                ExpandedMemory.SetUint8(destAddress + (uint)offset, ExpandedMemory.GetUint8(sourceAddress + (uint)offset));
            }
        }

        return overlap ? EmsErrors.EmmMoveOverlap : EmsErrors.EmmNoError;
    }
    
    public byte CopyEmsToConventionalMemory(int sourcePage, int sourcePageOffset, uint destAddress, int length) {
        switch (length)
        {
            case < 0:
                throw new ArgumentOutOfRangeException(nameof(length));
            case 0:
                return 0;
        }

        if (destAddress + length > MainMemory.ConvMemorySize) {
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
                return EmsErrors.EmsLogicalPageOutOfRange;
            }

            for (int i = 0; i < size; i++) {
                ExpandedMemory.SetUint8(sourceCount++, source[offset + i]);
            }

            length -= size;
            pageIndex++;
            offset = 0;
        }

        return 0;
    }
    
    public byte CopyConventionalMemoryToEmsMemory(uint sourceAddress, int destPage, int destPageOffset, int length) {
        switch (length)
        {
            case < 0:
                throw new ArgumentOutOfRangeException(nameof(length));
            case 0:
                return 0;
        }

        if (sourceAddress + length > MainMemory.ConvMemorySize) {
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
                target[offset + i] = ExpandedMemory.GetUint8(sourceCount++);
            }

            length -= size;
            pageIndex++;
            offset = 0;
        }

        return 0;
    }
    
    public byte CopyEmsToEms(EmsHandle srcHandle, int sourcePage, int sourcePageOffset, EmsHandle destHandle, int destPage, int destPageOffset, int length) {
        switch (length)
        {
            case < 0:
                throw new ArgumentOutOfRangeException(nameof(length));
            case 0:
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
                    return EmsErrors.EmsLogicalPageOutOfRange;
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

        return overlap ? EmsErrors.EmmMoveOverlap : EmsErrors.EmmNoError;
    }
}
