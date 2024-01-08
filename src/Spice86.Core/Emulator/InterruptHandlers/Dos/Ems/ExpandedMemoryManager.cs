namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Linq;

/// <summary>
/// Provides DOS applications with EMS memory. <br/>
/// Expanded memory is memory beyond DOS's 640K-byte limit.  This LIM <br/>
/// implementation supports 8 MB of expanded memory. <br/>
/// Because the 8086, 8088, and 80286 (in real mode) microprocessors can <br/>
/// physically address only 1M byte of memory, they access expanded memory <br/>
/// through a window in their physical address range.
/// <remarks>This is a LIM standard implementation. Which means there's no
/// difference between EMM pages and raw pages. They're both 16 KB.</remarks>
/// </summary>
public sealed class ExpandedMemoryManager : InterruptHandler {
    /// <summary>
    /// The string identifier in main memory for the EMS Handler. <br/>
    /// DOS programs can detect the presence of an EMS handler by looking for it <br/>
    /// (this is one, of two, methods to do so).
    /// </summary>
    public const string EmsIdentifier = "EMMXXXX0";

    /// <summary>
    /// The Emm Page Frame is 64 KB of EMM Memory, available at 0xE000 (segmented address).
    /// </summary>
    public const uint EmmPageFrameSize = 64 * 1024;

    /// <summary>
    /// Expands to 4.
    /// </summary>
    public const byte EmmMaxPhysicalPages = (byte) (EmmPageFrameSize / EmmPageSize);

    /// <summary>
    /// Value used when a page is unmapped or unallocated.
    /// </summary>
    public const ushort EmmNullPage = 0xFFFF;

    /// <summary>
    /// Value used when an EMM handle is unmapped un unallocated.
    /// </summary>
    public const ushort EmmNullHandle = 0xFFFF;

    /// <summary>
    /// The start address of the Emm Page Frame, as a segment value. <br/>
    /// The page frame is located above 640K bytes.  Normally, only video <br/>
    /// adapters, network cards, and similar devices exist between 640K and 1024K.
    /// </summary>
    public const ushort EmmPageFrameSegment = 0xE000;

    /// <summary>
    /// The size of an EMM logical or physical page: 16 KB.
    /// </summary>
    public const ushort EmmPageSize = 16384;

    /// <summary>
    /// This is required so EMS can be detected by DOS applications. <br/>
    /// (this is one, of two, methods to do so).
    /// </summary>
    public const ushort DosDeviceSegment = 0xF100;

    /// <inheritdoc />
    public override byte VectorNumber => 0x67;

    /// <summary>
    /// Because the 8086, 8088, and 80286 (in real mode) microprocessors can
    /// physically address only 1M byte of memory, they access expanded memory
    /// through a window in their physical address range. <br/>
    /// This is referred as the Emm Page Frame.
    /// </summary>
    public IDictionary<ushort, EmmRegister> EmmPageFrame { get; init; } = new Dictionary<ushort, EmmRegister>();

    /// <summary>
    /// This is the copy of the page frame. <br/>
    /// We copy the Emm Page Frame into it in the Save Page Map function. <br/>
    /// We restore this copy into the Emm Page Frame in the Restore Page Map function.
    /// </summary>
    public IDictionary<ushort, EmmRegister> EmmPageFrameSave { get; init; } = new Dictionary<ushort, EmmRegister>();

    /// <summary>
    /// The EMM handles given to the DOS programs. An EMM Handle has one or more unique logical pages.
    /// </summary>
    public IDictionary<int, EmmHandle> EmmHandles { get; } = new Dictionary<int, EmmHandle>();

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="dos">The DOS kernel.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public ExpandedMemoryManager(IMemory memory, Cpu cpu, Dos dos, ILoggerService loggerService) : base(memory, cpu, loggerService) {
        var device = new CharacterDevice(DeviceAttributes.Ioctl, EmsIdentifier, loggerService);
        dos.AddDevice(device, DosDeviceSegment, 0x0000);
        FillDispatchTable();

        // Allocation of system handle 0.
        AllocatePages(4);

        for (ushort i = 0; i < EmmMaxPhysicalPages; i++) {
            uint startAddress = MemoryUtils.ToPhysicalAddress(EmmPageFrameSegment, (ushort) (EmmPageSize * i));
            EmmRegister emmRegister = new(new EmmPage(EmmPageSize), startAddress);
            EmmPageFrame.Add(i, emmRegister);
            _memory.RegisterMapping(startAddress, EmmPageSize, emmRegister);
        }
    }

    /// <inheritdoc />
    public override SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        // We are going to install the handler elsewhere.
        // Let's make a backup so that other interrupt handlers don't get installed in our space.
        SegmentedAddress beginningAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.CurrentAddress = new SegmentedAddress(DosDeviceSegment, 0);
        // Install the handler normally, at our address
        SegmentedAddress handlerAddress = base.WriteAssemblyInRam(memoryAsmWriter);
        // Restore the address
        memoryAsmWriter.CurrentAddress = beginningAddress;
        return handlerAddress;
    }

    private void FillDispatchTable() {
        AddAction(0x40, GetStatus);
        AddAction(0x41, GetPageFrameSegment);
        AddAction(0x42, GetUnallocatedPageCount);
        AddAction(0x43, AllocatePages);
        AddAction(0x44, MapUnmapHandlePage);
        AddAction(0x45, DeallocatePages);
        AddAction(0x46, GetEmmVersion);
        AddAction(0x47, SavePageMap);
        AddAction(0x48, RestorePageMap);
        AddAction(0x4B, GetEmmHandleCount);
        AddAction(0x4C, GetHandlePages);
        AddAction(0x4D, GetAllHandlePages);
        AddAction(0x50, MapUnmapMultipleHandlePages);
        AddAction(0x51, ReallocatePages);
        AddAction(0x53, GetSetHandleName);
        AddAction(0x59, GetExpandedMemoryHardwareInformation);
    }

    /// <inheritdoc />
    public override void Run() {
        byte operation = _state.AH;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("EMS function: 0x{@Function:X2} AL=0x{Al:X2}", operation, _state.AL);
        }

        if (!HasRunnable(operation)) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("EMS function not provided: {@Function}", operation);
            }
            _state.AH = EmmStatus.EmmFunctionNotSupported;
        }
        Run(operation);
    }

    /// <summary>
    /// Returns in _state.AH whether the expanded memory manager is working correctly.
    /// </summary>
    public void GetStatus() {
        // Return good status in AH.
        _state.AH = EmmStatus.EmmNoError;
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("EMS: {@MethodName}: {@Result}", nameof(GetStatus), _state.AH);
        }
    }

    /// <summary>
    /// Returns in _state.BX where the 64KB EMM Page Frame is located.
    /// </summary>
    public void GetPageFrameSegment() {
        // Return page frame segment in BX.
        _state.BX = EmmPageFrameSegment;
        // Set good status.
        _state.AH = EmmStatus.EmmNoError;
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("EMS: {@MethodName}: 0x{Result:X4}", nameof(GetPageFrameSegment), _state.BX);
        }
    }

    /// <summary>
    /// The Get Unallocated Page Count function returns in _state.BX the number of
    /// unallocated pages (pages available to your program),
    /// and the total number of pages in expanded memory, in _state.DX.
    /// </summary>
    public void GetUnallocatedPageCount() {
        // Return total number of pages in DX.
        _state.DX = EmmMemory.TotalPages;
        // Return number of pages available in BX.
        _state.BX = GetFreePageCount();
        // Set good status.
        _state.AH = EmmStatus.EmmNoError;
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("{MethodName}: Total pages: {TotalPages} Free pages: {FreePages}", nameof(GetUnallocatedPageCount), _state.DX, _state.BX);
        }
    }

    /// <summary>
    /// Gets the number of pages available.
    /// </summary>
    /// <returns>The number of pages available.</returns>
    public ushort GetFreePageCount() {
        return (ushort) Math.Max(0, EmmMemory.TotalPages - EmmHandles.Sum(static x => x.Value.LogicalPages.Count));
    }

    /// <summary>
    /// The Allocate Pages function allocates the number of pages requested
    /// and assigns a unique EMM handle to these pages.  The EMM handle owns
    /// these pages until the DOS application deallocates them. <br/>
    /// It returns an unique EMM handle in _state.BX, &gt; 0 and &lt; 256. <br/>
    /// Parameters: <br/>
    /// _state.DX: The number of pages to allocate to the handle.
    /// </summary>
    public void AllocatePages() {
        ushort numberOfPagesToAlloc = _state.BX;
        if (numberOfPagesToAlloc is 0) {
            _state.AH = EmmStatus.EmmTriedToAllocateZeroPages;
            return;
        }
        if (EmmHandles.Count == EmmMemory.TotalPages) {
            _state.AH = EmmStatus.EmmOutOfHandles;
            return;
        }
        if (numberOfPagesToAlloc > EmmMemory.TotalPages) {
            _state.AH = EmmStatus.EmmNotEnoughPages;
            return;
        }
        _state.DX = AllocatePages(numberOfPagesToAlloc).HandleNumber;
        _state.AH = EmmStatus.EmmNoError;
    }

    public EmmHandle AllocatePages(ushort numberOfPagesToAlloc, EmmHandle? newHandle = null) {
        int key = newHandle?.HandleNumber ?? EmmHandles.Count;
        newHandle ??= new() {
            HandleNumber = (ushort)key
        };
        while (numberOfPagesToAlloc > 0) {
            ushort pageNumber = (ushort)newHandle.LogicalPages.Count;
            newHandle.LogicalPages.Add(new EmmPage(EmmPageSize){
                PageNumber = pageNumber
            });
            numberOfPagesToAlloc--;
        }

        if (!EmmHandles.TryAdd(key, newHandle)) {
            EmmHandles[key] = newHandle;
        }
        return newHandle;
    }

    /// <summary>
    /// The Map/Unmap Handle Page function maps a logical page at a specific
    /// physical page anywhere in the mappable regions of system memory. <br/>
    /// The lowest valued physical page numbers are associated with regions of
    /// memory outside the conventional memory range.  Use Function 25 (Get Mappable Physical Address Array)
    /// to determine which physical pages within a system are mappable and determine the segment addresses which
    /// correspond to a specific physical page number.  Function 25 provides a
    /// cross reference between physical page numbers and segment addresses. <br/>
    /// This function can also unmap physical pages, making them inaccessible for reading or writing. <br/>
    /// You unmap a physical page by setting its associated logical page to FFFFh.
    /// </summary>
    /// <remarks>
    /// The handle determines what type of pages are being mapped. <br/>
    /// Logical pages allocated by Function 4 and Function 27 (Allocate Standard Pages
    /// subFunction) are referred to as pages and are 16K bytes long. <br/>
    /// Logical pages allocated by Function 27 (Allocate Raw Pages subFunction) are
    /// referred to as raw pages and might not be the same size as logical pages.
    /// </remarks>
    public void MapUnmapHandlePage() {
        ushort physicalPageNumber = _state.AL;
        ushort logicalPageNumber = _state.BX;
        ushort handleId = _state.DX;
        _state.AH = MapUnmapHandlePage(logicalPageNumber, physicalPageNumber, handleId);
    }

    public byte MapUnmapHandlePage(ushort logicalPageNumber, ushort physicalPageNumber, ushort handleId) {
        if (physicalPageNumber > EmmPageFrame.Count) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Physical page {PhysicalPage} out of range",
                    physicalPageNumber);
            }
            return EmmStatus.EmmIllegalPhysicalPage;
        }

        if (!IsValidHandle(handleId)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Invalid Handle {InvalidHandle}", handleId);
            }
            return EmmStatus.EmmInvalidHandle;
        }

        EmmRegister emmRegister = EmmPageFrame[physicalPageNumber];

        // Unmapping
        if (logicalPageNumber == EmmNullPage) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Unmapped physical page: {PhysicalPage}",
                    emmRegister.PhysicalPage.PageNumber);
            }
            emmRegister.PhysicalPage.PageNumber = EmmNullPage;
            return EmmStatus.EmmNoError;
        }

        // Mapping
        EmmHandle allocatedEmmHandle = EmmHandles[handleId];
        if (logicalPageNumber > allocatedEmmHandle.LogicalPages.Count - 1) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Logical page {LogicalPage} out of range",
                    logicalPageNumber);
            }
            return EmmStatus.EmmLogicalPageOutOfRange;
        }
        emmRegister.PhysicalPage = allocatedEmmHandle.LogicalPages[logicalPageNumber];
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Mapped logical page {LogicalPage} to physical page {PhysicalPage}",
                logicalPageNumber,
                physicalPageNumber);
        }

        return EmmStatus.EmmNoError;
    }

    private bool IsValidHandle(ushort handleId) => EmmHandles.ContainsKey(handleId);

    public void MapSegment(ushort logicalPage, ushort segment, ushort handleId) {
        uint address = MemoryUtils.ToPhysicalAddress(segment, 0);
        uint pageFrameAddress = MemoryUtils.ToPhysicalAddress(EmmPageFrameSegment, 0);
        uint pageFrameAddressEnd = MemoryUtils.ToPhysicalAddress(EmmPageFrameSegment, EmmPageSize);
        if (address >= pageFrameAddress && address < pageFrameAddressEnd) {
            MapUnmapHandlePage(logicalPage, (ushort) ((address - pageFrameAddress) / EmmPageSize), handleId);
        }
    }

    /// <summary>
    /// Deallocate Pages deallocates the logical pages currently allocated to
    /// an EMM handle.  Only after the application deallocates these pages can
    /// other applications use them.  When a handle is deallocated, its name is
    /// set to all ASCII nulls (binary zeros).
    /// </summary>
    /// <remarks>
    /// A program must perform this function before it exits to DOS. If it
    /// doesn't, no other programs can use these pages or the EMM handle.
    /// This means that a program using expanded memory should trap critical
    /// errors and control-break if there is a chance that the program will
    /// have allocated pages when either of these events occur.
    /// </remarks>
    public void DeallocatePages() {
        ushort handleId = _state.DX;
        if (!IsValidHandle(handleId)) {
            _state.AH = EmmStatus.EmmInvalidHandle;
            return;
        }
        if (EmmHandles[handleId].SavedPageMap) {
            _state.AH = EmmStatus.EmmSaveMapError;
            return;
        }
        EmmHandles.Remove(handleId);
        _state.AH = EmmStatus.EmmNoError;
    }

    /// <summary>
    /// Returns the LIM specs version we implement (3.2) in _state.AL. <br/>
    /// </summary>
    public void GetEmmVersion() {
        // Return EMS version 3.2.
        _state.AL = 0x32;
        // Return good status.
        _state.AH = EmmStatus.EmmNoError;
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("EMS: {@MethodName}: 0x{Version:X2}", nameof(GetEmmVersion), _state.AL);
        }
    }

    /// <summary>
    /// Save Page Map saves the contents of the page mapping registers on all
    /// expanded memory boards in an internal save area.  The function is
    /// typically used to save the memory mapping context of the EMM handle
    /// that was active when a software or hardware interrupt occurred.  (See
    /// Function 9, Restore Page Map, for the restore operation.) <br/>
    /// If you're writing a resident program, an interrupt service, or a device driver
    /// that uses expanded memory, you must save the state of the mapping
    /// hardware.  You must save this state because application software using
    /// expanded memory may be running when your program is invoked by a
    /// hardware interrupt, a software interrupt, or DOS. <br/>
    /// The Save Page Map function requires the EMM handle that was assigned
    /// to your resident program, interrupt service routine, or device driver
    /// at the time it was initialized. This is not the EMM handle that the
    /// application software was using when your software interrupted it. <br/>
    /// The Save Page Map function saves the state of the map registers for
    /// only the 64K-byte page frame defined in versions 3.x of this
    /// specification.  Since all applications written to LIM versions 3.x
    /// require saving the map register state of only this 64K-byte page
    /// frame, saving the entire mapping state for a large number of mappable
    /// pages would be inefficient use of memory. <br/>
    /// </summary>
    public void SavePageMap() {
        _state.AH = SavePageMap(_state.DX);
    }

    public byte SavePageMap(ushort handleId) {
        if (!IsValidHandle(handleId)) {
            if (handleId != 0) {
                return EmmStatus.EmmInvalidHandle;
            }
        }

        if (EmmHandles[handleId].SavedPageMap) {
            return EmmStatus.EmmPageMapSaved;
        }

        EmmPageFrameSave.Clear();

        foreach (KeyValuePair<ushort, EmmRegister> item in EmmPageFrame) {
            EmmPageFrameSave.Add(item);
        }

        EmmHandles[handleId].SavedPageMap = true;
        return EmmStatus.EmmNoError;
    }

    /// <summary>
    /// The Restore Page Map function restores the page mapping register
    /// contents on the expanded memory boards for a particular EMM handle. <br/>
    /// This function lets your program restore the contents of the mapping
    /// registers its EMM handle saved. (See Function 8, Save Page Map for the
    /// save operation.) <br/>
    /// If you're writing a resident program, an interrupt service routine, or
    /// a device driver that uses expanded memory, you must restore the
    /// mapping hardware to the state it was in before your program took over. <br/>
    /// You must save this state because application software using expanded
    /// memory might have been running when your program was invoked. <br/>
    /// The Restore Page Map function requires the EMM handle that was
    /// assigned to your resident program, interrupt service routine, or
    /// device driver at the time it was initialized.  This is not the EMM
    /// handle that the application software was using when your software interrupted it. <br/>
    /// The Restore Page Map function restores the state of the map registers
    /// for only the 64K-byte page frame defined in versions 3.x of this
    /// specification.  <br/>
    /// Since all applications written to LIM versions 3.x require restoring the map
    /// register state of only this 64K-byte page frame, restoring the entire mapping state
    /// for a large number of mappable pages would be inefficient use of memory.
    /// </summary>
    public void RestorePageMap() {
        _state.AH = RestorePageMap(_state.DX);
    }

    public byte RestorePageMap(ushort handleId) {
        if (!IsValidHandle(handleId)) {
            if (handleId != 0) {
                return EmmStatus.EmmInvalidHandle;
            }
        }

        if (EmmHandles[handleId].SavedPageMap) {
            return EmmStatus.EmmPageMapSaved;
        }

        EmmPageFrame.Clear();

        foreach (KeyValuePair<ushort, EmmRegister> item in EmmPageFrameSave) {
            EmmPageFrame.Add(item);
        }

        EmmHandles[handleId].SavedPageMap = false;
        return EmmStatus.EmmNoError;
    }

    /// <summary>
    /// Returns in _state.BX the number of open EMM handles.
    /// </summary>
    /// <remarks>This number will not exceed 255.</remarks>
    public void GetEmmHandleCount() {
        _state.BX = 0;
        _state.BX = (ushort)EmmHandles.Count;
        // Return good status.
        _state.AH = EmmStatus.EmmNoError;
    }

    /// <summary>
    /// The Get Handle Pages function returns in _state.BX, the number of pages allocated to
    /// a specific EMM handle. <br/>
    /// Params: <br/>
    /// _state.DX: The EMM Handle.
    /// </summary>
    /// <remarks>
    /// _state.BX contains the number of logical pages allocated to the
    /// specified EMM handle.  This number never exceeds 512
    /// because the memory manager allows a maximum of 512 pages
    /// (8 MB) of expanded memory.
    /// </remarks>
    public void GetHandlePages() {
        _state.BX = (ushort)EmmHandles[_state.DX].LogicalPages.Count;
        _state.AX = EmmStatus.EmmNoError;
    }

    /// <summary>
    /// The Get All Handle Pages function returns an array of the open EMM
    /// handles and the number of pages allocated to each one.
    /// </summary>
    public void GetAllHandlePages() {
        _state.BX = GetAllocatedHandlePagesCount();
        _state.AH = GetAllHandlePages(MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI));
        _state.AX = EmmStatus.EmmNoError;
    }

    public byte GetAllHandlePages(uint tableAddress) {
        foreach(KeyValuePair<int, EmmHandle> allocatedHandle in EmmHandles) {
            _memory.UInt16[tableAddress] = allocatedHandle.Value.HandleNumber;
            tableAddress += 2;
            _memory.UInt16[tableAddress] = (ushort)allocatedHandle.Value.LogicalPages.Count;
            tableAddress += 2;
        }
        return EmmStatus.EmmNoError;
    }

    /// <summary>
    /// This function can, in a single invocation, map (or unmap) logical
    /// pages into as many physical pages as the system supports. <br/>
    /// Consequently, it has less execution overhead than mapping pages one at
    /// a time.  For applications which do a lot of page mapping, this is the
    /// preferred mapping method.
    /// </summary>
    public void MapUnmapMultipleHandlePages() {
        byte operation = _state.AL;
        ushort handleId = _state.DX;
        ushort numberOfPages = _state.CX;
        uint mapAddress = MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI);
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "EMS: {@MethodName} Map {@NumberOfPages} pages from handle {@Handle} according to the map at address 0x{@MapAddress:X6}",
                nameof(MapUnmapMultipleHandlePages), numberOfPages, handleId, mapAddress);
        }

        _state.AH = EmmStatus.EmmNoError;
        switch (operation) {
            case EmmSubFunctions.UsePhysicalPageNumbers:
                for (int i = 0; i < numberOfPages; i++) {
                    ushort logicalPage = _memory.UInt16[mapAddress];
                    mapAddress += 2;
                    ushort physicalPage = _memory.UInt16[mapAddress];
                    mapAddress += 2;
                    MapUnmapHandlePage(logicalPage, physicalPage, handleId);
                    if (_state.AH != EmmStatus.EmmNoError) {
                        break;
                    }
                }
                break;
            case EmmSubFunctions.UseSegmentedAddress:
                for (int i = 0; i < numberOfPages; i++) {
                    ushort logicalPage = _memory.UInt16[mapAddress];
                    mapAddress += 2;
                    ushort segment = _memory.UInt16[mapAddress];
                    mapAddress += 2;
                    MapSegment(logicalPage, segment, handleId);
                    if (_state.AH != EmmStatus.EmmNoError) {
                        break;
                    }
                }
                break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error(
                        "{@MethodName} subFunction number {@SubFunctionId} not supported",
                        nameof(MapUnmapMultipleHandlePages), operation);
                }
                _state.AH = EmmStatus.EmmInvalidSubFunction;
                break;
        }
    }

    /// <summary>
    /// This function allows an application program to increase or decrease
    /// (reallocate) the number of logical pages allocated to an EMM handle.
    /// </summary>
    public void ReallocatePages() {
        ushort handleId = _state.DX;
        ushort reallocationCount = _state.BX;
        if (!IsValidHandle(handleId)) {
            _state.AH = EmmStatus.EmmInvalidHandle;
            return;
        }

        if (EmmHandles.Count == EmmMemory.TotalPages) {
            _state.AH = EmmStatus.EmmOutOfHandles;
            return;
        }
        if (reallocationCount > EmmMemory.TotalPages) {
            _state.AH = EmmStatus.EmmNotEnoughPages;
            return;
        }

        EmmHandle handle = EmmHandles[handleId];

        if (handle.LogicalPages.Count == reallocationCount) {
            _state.AH = EmmStatus.EmmNoError;
            return;
        }

        if (reallocationCount == 0) {
            handle.LogicalPages.Clear();
        }

        if (reallocationCount > handle.LogicalPages.Count) {
            ushort addedPages = (ushort)(reallocationCount - handle.LogicalPages.Count);
            AllocatePages(addedPages, handle);
        }

        if (reallocationCount < handle.LogicalPages.Count) {
            ushort removedPages = (ushort)(handle.LogicalPages.Count - reallocationCount);
            while (removedPages > 0) {
                if (handle.LogicalPages.Any()) {
                    handle.LogicalPages.Remove(handle.LogicalPages.Last());
                }
                removedPages--;
            }
        }

        _state.AH = EmmStatus.EmmNoError;
    }

    /// <summary>
    /// This subFunction gets the eight character name currently assigned to a handle.
    /// There is no restriction on the characters which may be used
    /// in the handle name (that is, anything from 00h through FFh). <br/>
    /// The handle name is initialized to ASCII nulls (binary zeros) three
    /// times: when the memory manager is installed, when a handle is
    /// allocated, and when a handle is deallocated. <br/>
    /// A handle with a name which is all ASCII nulls, by definition, has no name. <br/>
    /// When a handle is assigned a name, at least one character in the name must be a non-null character,
    /// in order to distinguish it from a handle without a name.
    /// </summary>
    public void GetSetHandleName() {
        ushort handle = _state.DX;
        _state.AH = GetSetHandleName(handle, _state.AL);
    }

    /// <summary>
    /// Gets or Set a handle name, depending on the <paramref name="operation"/>
    /// </summary>
    /// <param name="handleId">The handle reference</param>
    /// <param name="operation">Get: 0, Set: 1</param>
    /// <returns>The state of the operation</returns>
    public byte GetSetHandleName(ushort handleId, byte operation) {
        if (!IsValidHandle(handleId)) {
            return EmmStatus.EmmInvalidHandle;
        }
        switch (operation) {
            case EmmSubFunctions.HandleNameGet:
                GetHandleName(handleId);
                break;

            case EmmSubFunctions.HandleNameSet:
                SetHandleName(handleId,
                    _memory.GetZeroTerminatedString(MemoryUtils.ToPhysicalAddress(_state.SI, _state.DI),
                        8));
                break;

            default:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("{@MethodName}: subFunction {@FunctionId} invalid", nameof(GetSetHandleName), operation);
                }
                return EmmStatus.EmmInvalidSubFunction;
        }
        return EmmStatus.EmmNoError;
    }

    /// <summary>
    /// This function is for use by operating systems only.  This function can
    /// be disabled at any time by the operating system. <br/>
    /// Refer to Function 30 for a description of how an operating system does this.
    /// </summary>
    public void GetExpandedMemoryHardwareInformation() {
        switch (_state.AL) {
            case EmmSubFunctions.GetHardwareConfigurationArray:
                uint data = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI);
                // 1 page is 1K paragraphs (16KB)
                _memory.UInt16[data] =0x0400;
                data+=2;
                // No alternate register sets
                _memory.UInt16[data] = 0x0000;
                data+=2;
                // Context save area size
                _memory.UInt16[data] = (ushort)EmmHandles.SelectMany(static x => x.Value.LogicalPages).Count();
                data+=2;
                // No DMA channels
                _memory.UInt16[data] = 0x0000;
                data+=2;
                // Always 0 for LIM standard
                _memory.UInt16[data] = 0x0000;
                break;
            case EmmSubFunctions.GetUnallocatedRawPages:
                // Return number of pages available in BX.
                _state.BX = GetFreePageCount();
                // Return total number of pages in DX.
                _state.DX = EmmMemory.TotalPages;
                // Set good status.
                _state.AH = EmmStatus.EmmNoError;
                break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("{@MethodName}: EMS subfunction number {@SubFunction} not implemented",
                        nameof(GetExpandedMemoryHardwareInformation), _state.AL);
                }
                break;
        }
    }

    /// <summary>
    /// Returns the number of open EMM handles
    /// </summary>
    /// <returns>The number of open EMM handles</returns>
    public ushort GetAllocatedHandlePagesCount() => (ushort) EmmHandles.Sum(static x => x.Value.LogicalPages.Count);

    /// <summary>
    /// Gets the name of a handle.
    /// </summary>
    public string GetHandleName(ushort handle) {
        EmmHandles[handle].Name = _memory.GetZeroTerminatedString(MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI), 8);
        return EmmHandles[handle].Name;
    }

    /// <summary>
    /// Set the name of a handle.
    /// </summary>
    public void SetHandleName(ushort handle, string name) {
        EmmHandles[handle].Name = name;
    }
}