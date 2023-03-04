namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using System.Numerics;
using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System.Linq;

/// <summary>
/// Provides DOS applications with EMS memory.
/// </summary>
public sealed class ExpandedMemoryManager : InterruptHandler {
    public static class EmsSubFunctions {
        public const byte MapUnmapPages = 0x00;
        public const byte HandleNameGet = 0x00;
        public const byte HandleNameSet = 0x01;
        public const byte GetHardwareInformationUnallocatedRawPages = 0x01;
        public const byte MoveExchangeMove = 0x00;
    }

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
    public const int MaximumLogicalPages = 1024;

    public const ushort PageFrameSegment = 0xE000;
    public const int FirstHandle = 1;
    public const int LastHandle = 254;
    public const int SegmentsPerPage = PageSize / 16;

    public const string EmsIdentifier = "EMMXXXX0";

    public Memory ExpandedMemory { get; init; }

    public override ushort? InterruptHandlerSegment => 0xF100;

    private readonly short[] _pageOwners = new short[MaximumLogicalPages];
    private readonly SortedList<int, EmsHandle> _handles = new();
    private readonly byte[]?[] _mappedPages = new byte[MaximumLogicalPages][];
    
    private readonly ILoggerService _loggerService;
    
    public ExpandedMemoryManager(Machine machine, ILoggerService loggerService) : base(machine) {
        _loggerService = loggerService;
        ExpandedMemory = new(machine,8 * 1024);
        MemoryUtils.SetZeroTerminatedString(machine.MainMemory.Ram, MemoryUtils.ToPhysicalAddress(0xF100 - PageFrameSegment, 0x000A), EmsIdentifier, EmsIdentifier.Length + 1);

        var device = new CharacterDevice(DeviceAttributes.Ioctl, EmsIdentifier);
        machine.Dos.AddDevice(device, InterruptHandlerSegment, 0x0000);
        FillDispatchTable();
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x40, new Callback(0x40, GetStatus));
        _dispatchTable.Add(0x41, new Callback(0x41, GetPageFrameSegment));
        _dispatchTable.Add(0x42, new Callback(0x42, GetNumberOfUnallocatedPages));
        _dispatchTable.Add(0x43, new Callback(0x43, GetHandleAndAllocatePages));
        _dispatchTable.Add(0x44, new Callback(0x44, MapUnmapHandlePage));
        _dispatchTable.Add(0x45, new Callback(0x45, ReleaseHandleAndFreePages));
        _dispatchTable.Add(0x46, new Callback(0x46, GetEmmVersion));
        _dispatchTable.Add(0x47, new Callback(0x47, SavePageMap));
        _dispatchTable.Add(0x48, new Callback(0x48, RestorePageMap));
        _dispatchTable.Add(0x4B, new Callback(0x4B, GetHandleCount));
        _dispatchTable.Add(0x4C, new Callback(0x4C, GetPagesForOneHandle));
        _dispatchTable.Add(0x50, new Callback(0x50, MapOrUnmapMultiplePageMap));
        _dispatchTable.Add(0x51, new Callback(0x51, ReallocatePages));
        _dispatchTable.Add(0x53, new Callback(0x53, SetGetHandleName));
        _dispatchTable.Add(0x57, new Callback(0x57, MemoryRegion));
        _dispatchTable.Add(0x59, new Callback(0x59, GetHardwareInformation));
    }

    public bool TryGetMappedPageData(uint address, out uint data) {
        if (address is < (PageFrameSegment << 4) or >= (PageFrameSegment << 4) + 65536) {
            data = 0;
            return false;
        }
        data = _machine.EmsCard.ExpandedMemory.GetUint32(address);
        return true;
    }

    public bool TryGetMappedPageData(uint address, out ushort data) {
        if (address is < (PageFrameSegment << 4) or >= (PageFrameSegment << 4) + 65536) {
            data = 0;
            return false;
        }
        data = _machine.EmsCard.ExpandedMemory.GetUint16(address);
        return true;
    }
    
    public bool TryGetMappedPageData(uint address, out byte data) {
        if (address is < (PageFrameSegment << 4) or >= (PageFrameSegment << 4) + 65536) {
            data = 0;
            return false;
        }
        data = _machine.EmsCard.ExpandedMemory.GetUint8(address);
        return true;
    }

    public bool TryWriteMappedPageData<T>(uint address, T data) where T : INumber<T> {
        if (address is < (PageFrameSegment << 4) or >= (PageFrameSegment << 4) + 65536) {
            return false;
        }
        switch (data)
        {
            case byte b:
                _machine.EmsCard.ExpandedMemory.SetUint8(address, b);
                break;
            case ushort u:
                _machine.EmsCard.ExpandedMemory.SetUint16(address, u);
                break;
            case uint i:
                _machine.EmsCard.ExpandedMemory.SetUint32(address, i);
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

    public void GetNumberOfUnallocatedPages() {
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

        
        if (pagesRequested < MaximumLogicalPages)
        {
            ushort handle = _state.DX;
            if (_handles.TryGetValue(handle, out var emsHandle))
            {
                emsHandle.Reallocate(pagesRequested);

                // Return good status.
                _state.AH = EmsErrors.EmmNoError;
            }
            else
            {
                // Return "couldn't find specified handle" c
                _state.AH = EmsErrors.EmmInvalidHandle;
            }
        }
        else
        {
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
                EmsHandle handle = new EmsHandle(pagesRequested);
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
    public void MapUnmapHandlePage() {
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
            byte[]? logicalPage = handle.GetLogicalPage(logicalPageIndex);
            if(logicalPage == null) {
                // Return "logical page out of range" code.
                _state.AH = EmsErrors.EmsLogicalPageOutOfRange;
                return;
            }

            MapPage(logicalPage, physicalPage);
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
    public void MapPage(byte[] logicalPage, int physicalPageIndex) {
        // If the requested logical page is already mapped, it needs to get unmapped first.
        UnmapLogicalPage(logicalPage);

        // If a page is already mapped, make sure it gets unmapped first.
        UnmapPage(physicalPageIndex);

        ushort segment = (ushort)(PageFrameSegment + SegmentsPerPage * physicalPageIndex);
        Span<byte> dest = _machine.EmsCard.ExpandedMemory.GetSpan(segment, PageSize);
        logicalPage.CopyTo(dest);
        _mappedPages[physicalPageIndex] = logicalPage;
    }

    /// <summary>
    /// Copies data from a physical page to a logical page.
    /// </summary>
    /// <param name="physicalPageIndex">Physical page to copy from.</param>
    public void UnmapPage(int physicalPageIndex) {
        byte[]? currentPage = _mappedPages[physicalPageIndex];
        if (currentPage != null) {
            ushort segment = (ushort)(PageFrameSegment + SegmentsPerPage * physicalPageIndex);
            Span<byte> src = _machine.EmsCard.ExpandedMemory.GetSpan(segment, PageSize);
            src.CopyTo(currentPage);
            _mappedPages[physicalPageIndex] = null;
        }
    }

    /// <summary>
    /// Unmaps a specific logical page if it is currently mapped.
    /// </summary>
    /// <param name="logicalPage">Logical page to unmap.</param>
    public void UnmapLogicalPage(byte[] logicalPage) {
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
            MemoryUtils.SetZeroTerminatedString(this._machine.EmsCard.ExpandedMemory.Ram, MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI), handle.Name, handle.Name.Length + 1);
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
            handle.Name = MemoryUtils.GetZeroTerminatedString(_machine.EmsCard.ExpandedMemory.Ram, MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI), 8);
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

        uint arraySegment = _state.DS;
        uint arrayOffset = _state.SI;
        for (int i = 0; i < pageCount; i++) {
            ushort logicalPageIndex = _machine.EmsCard.ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress((ushort) arraySegment, (ushort) arrayOffset));
            ushort physicalPageIndex = _machine.EmsCard.ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress((ushort) arraySegment, (ushort)(arrayOffset + 2u)));

            if (physicalPageIndex >= MaximumPhysicalPages) {
                // Return "physical page out of range" code.
                _state.AH = EmsErrors.EmsIllegalPhysicalPage;
                return;
            }

            if (logicalPageIndex != 0xFFFF) {
                byte[]? logicalPage = handle.GetLogicalPage(logicalPageIndex);
                if (logicalPage == null) {
                    // Return "logical page out of range" code.
                    _state.AH = EmsErrors.EmsLogicalPageOutOfRange;
                    return;
                }

                MapPage(logicalPage, physicalPageIndex);
            } else {
                UnmapPage(physicalPageIndex);
            }

            arrayOffset = arrayOffset + 4u;
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
        handle.SavedPageMap = _mappedPages.ToArray();

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
            byte[]? pageMap = handle.SavedPageMap?.ElementAt(i);
            if (pageMap != null && pageMap != _mappedPages[i]) {
                MapPage(pageMap, i);
            }
        }

        // Return good status.
        _state.AH = EmsErrors.EmmNoError;
    }
    /// <summary>
    /// Copies a block of memory.
    /// </summary>
    public void Move() {
        int length = (int)_machine.EmsCard.ExpandedMemory.GetUint32(MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI));

        byte sourceType = _machine.EmsCard.ExpandedMemory.GetUint8(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 4u)));
        int sourceHandleIndex = _machine.EmsCard.ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 5u)));
        int sourceOffset = _machine.EmsCard.ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 7u)));
        int sourcePage = _machine.EmsCard.ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 9u)));

        byte destType = _machine.EmsCard.ExpandedMemory.GetUint8(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 11u)));
        int destHandleIndex = _machine.EmsCard.ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 12u)));
        int destOffset = _machine.EmsCard.ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 14u)));
        int destPage = _machine.EmsCard.ExpandedMemory.GetUint16(MemoryUtils.ToPhysicalAddress(_state.DS, (ushort)(_state.SI + 16u)));

        CopyDataFromMappedConvMemoryToEmsPages();

        if (sourceType == 0 && destType == 0) {
            _state.AH = EmsCopier.ConvToConv(_machine.EmsCard.ExpandedMemory, (uint)((sourcePage << 4) + sourceOffset), (uint)((destPage << 4) + destOffset), length);
        } else if (sourceType != 0 && destType == 0) {
            if (!_handles.TryGetValue(sourceHandleIndex, out EmsHandle? sourceHandle)) {
                // Return "couldn't find specified handle" code.
                _state.AH = EmsErrors.EmmInvalidHandle;
                return;
            }

            _state.AH = EmsCopier.EmsToConv(sourceHandle, sourcePage, sourceOffset, _machine.EmsCard.ExpandedMemory, (uint)((destPage << 4) + destOffset), length);
        } else if (sourceType == 0 && destType != 0) {
            if (!_handles.TryGetValue(destHandleIndex, out EmsHandle? sourceHandle)) {
                // Return "couldn't find specified handle" code.
                _state.AH = EmsErrors.EmmInvalidHandle;
                return;
            }

            _state.AH = EmsCopier.EmsToConv(sourceHandle, sourcePage, sourceOffset, _machine.EmsCard.ExpandedMemory, (uint)((destPage << 4) + destOffset), length);
        } else {
            if (!_handles.TryGetValue(sourceHandleIndex, out EmsHandle? sourceHandle) || !_handles.TryGetValue(destHandleIndex, out EmsHandle? destHandle)) {
                // Return "couldn't find specified handle" code.
                _state.AH = EmsErrors.EmmInvalidHandle;
                return;
            }

            _state.AH = EmsCopier.EmsToEms(sourceHandle, sourcePage, sourceOffset, destHandle, destPage, destOffset, length);
        }

        CopyDataFromEmsPagesToMappedConvMem();
    }
    
    public void CopyDataFromMappedConvMemoryToEmsPages() {
        for (byte i = 0; i < MaximumPhysicalPages; i++) {
            if (_mappedPages[i] != null) {
                ushort segment = (ushort)(PageFrameSegment + SegmentsPerPage * i);
                Span<byte> src = _machine.EmsCard.ExpandedMemory.GetSpan(segment, PageSize);
                src.CopyTo(_mappedPages[i]);
            }
        }
    }
    
    public void CopyDataFromEmsPagesToMappedConvMem() {
        for (byte i = 0; i < MaximumPhysicalPages; i++) {
            if (_mappedPages[i] != null) {
                ushort segment = (ushort)(PageFrameSegment + SegmentsPerPage * i);
                Span<byte> src = _machine.EmsCard.ExpandedMemory.GetSpan(segment, PageSize);
                src.CopyTo(_mappedPages[i]);
            }
        }
    }
    
    public ushort GetNextFreePage(short handle) {
        for (int i = 0; i < _pageOwners.Length; i++) {
            if (_pageOwners[i] == -1) {
                _pageOwners[i] = handle;
                return (ushort)i;
            }
        }

        return 0;
    }
}
