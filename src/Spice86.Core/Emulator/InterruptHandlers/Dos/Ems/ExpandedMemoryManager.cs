namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

/// <summary>
/// Provides DOS applications with EMS memory.
/// </summary>
public sealed class ExpandedMemoryManager : InterruptHandler {
    public const string EmsIdentifier = "EMMXXXX0";

    /// <summary>
    /// EMM system handle (reserved for OS usage)
    /// </summary>
    public const byte EmmSystemHandle = 0;

    public const byte EmmMaxHandles = 200;

    public const byte EmmMaxPhysicalPages = 4;

    public const ushort EmmNullPage = 0xFFFF;
    
    public const ushort EmmNullHandle = 0xFFFF;

    public const ushort EmmPageFrameSegment = 0xE000;

    public const ushort EmmPageSize = 16 * 1024;
    
    public const int EmmSegmentsPerPage = EmmPageSize / 16;

    public override ushort? InterruptHandlerSegment => 0xF100;
    
    public override byte Index => 0x67;

    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Type of EMS emulation. <br/>
    /// 0 = Mixed mode. <br/>
    /// 1 = EMS board. <br/>
    /// 2 = EMS386. <br/>
    /// </summary>
    public byte EmsType { get; init; } = 1;
    
    public MemoryBlock MemoryBlock { get; }

    public EmmMapping[] EmmSegmentMappings { get; } = new EmmMapping[0x40];

    public EmmMapping[] EmmMappings { get; } = new EmmMapping[EmmHandle.EmmMaxPhysicalPages];
    
    public EmmHandle[] EmmHandles { get; } = new EmmHandle[EmmMaxHandles];
    
    public ushort MemorySizeInMb { get; }

    public int TotalPages => MemoryBlock.Pages;

    public ushort GetFreePages() => Math.Min((ushort) 0x7FFF, (ushort) (GetFreeMemoryTotal() / 4));

    public ExpandedMemoryManager(Machine machine, ILoggerService loggerService) : base(machine) {
        _loggerService = loggerService;
        MemorySizeInMb = Math.Min((ushort)8, (ushort) (_memory.Size / 1024 / 1024));
        var device = new CharacterDevice(DeviceAttributes.Ioctl, EmsIdentifier);
        machine.Dos.AddDevice(device, InterruptHandlerSegment, 0x0000);
        for (int i = 0; i < EmmHandles.Length; i++) {
            EmmHandles[i] = new();
        }

        for (int i = 0; i < EmmMappings.Length; i++) {
            EmmMappings[i] = new();
        }

        for (int i = 0; i < EmmSegmentMappings.Length; i++) {
            EmmSegmentMappings[i] = new();
        }

        MemoryBlock = new(MemorySizeInMb);

        FillDispatchTable();
        
        _state.AH = EmmAllocateSystemHandle(8);
    }
    
    /// <summary>
    /// Allocates OS-dedicated handle (ems handle zero, 128kb) <br/>
    /// This handle is never deallocated.
    /// </summary>
    /// <param name="pages">The number of pages to initialize</param>
    /// <returns>Success or error code.</returns>
    private byte EmmAllocateSystemHandle(ushort pages) {
        /* Check for enough free pages */
        if ((GetFreeMemoryTotal() / 4) < pages) {
            return EmmStatus.EmmOutOfLogicalPages;
        }

        /* Release memory if already allocated */
        if (EmmHandles[EmmSystemHandle].Pages != EmmNullHandle) {
            ReleasePages(EmmHandles[EmmSystemHandle].MemHandle);
        }
        int mem = AllocatePages((ushort) (pages * 4), false);
        if (mem == 0) FailFastWithLogMessage("EMS: System handle memory allocation failure");
        EmmHandles[EmmSystemHandle].Pages = pages;
        EmmHandles[EmmSystemHandle].MemHandle = mem;
        return EmmStatus.EmmNoError;
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x40, new Callback(0x40, GetStatus));
        _dispatchTable.Add(0x41, new Callback(0x41, GetPageFrameSegment));
        _dispatchTable.Add(0x42, new Callback(0x42, GetNumberOfPages));
        _dispatchTable.Add(0x43, new Callback(0x43, GetHandleAndAllocatePages));
        _dispatchTable.Add(0x44, new Callback(0x44, MapExpandedMemoryPage));
        _dispatchTable.Add(0x45, new Callback(0x45, ReleaseHandleAndFreePages));
        _dispatchTable.Add(0x46, new Callback(0x46, GetEmmVersion));
        _dispatchTable.Add(0x47, new Callback(0x47, SavePageMap));
        _dispatchTable.Add(0x48, new Callback(0x48, RestorePageMap));
        _dispatchTable.Add(0x4B, new Callback(0x4B, GetHandleCount));
        _dispatchTable.Add(0x4C, new Callback(0x4C, GetPagesForOneHandle));
        _dispatchTable.Add(0x4D, new Callback(0x4D, GetPageForAllHandles));
        _dispatchTable.Add(0x4E, new Callback(0x4E, SaveOrRestorePageMap));
        _dispatchTable.Add(0x4F, new Callback(0x4F, SaveOrRestorePartialPageMap));
        _dispatchTable.Add(0x50, new Callback(0x50, MapOrUnmapMultipleHandlePages));
        _dispatchTable.Add(0x51, new Callback(0x51, ReallocatePages));
        _dispatchTable.Add(0x53, new Callback(0x53, SetGetHandleName));
        _dispatchTable.Add(0x54, new Callback(0x54, HandleFunctions));
        _dispatchTable.Add(0x58, new Callback(0x58, GetMappablePhysicalArrayAddressArray));
        _dispatchTable.Add(0x59, new Callback(0x59, GetHardwareInformation));
        _dispatchTable.Add(0x5A, new Callback(0x5A, AllocateStandardRawPages));
    }

    public override void Run() {
        byte operation = _state.AH;
        if (!_dispatchTable.ContainsKey(operation)) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("EMS function not provided: {@Function}", operation);
            }
            _state.AH = EmmStatus.EmmFuncNoSup;
        }
        Run(operation);
    }
    
    public ushort GetFreeMemoryTotal() {
        ushort free = 0;
        ushort index = 0;
        while (index < TotalPages) {
            if (MemoryBlock.MemoryHandles[index] == 0) {
                free++;
            }
            index++;
        }
        return free;
    }
    
    public void GetStatus() {
        // Return good status in AH.
        _state.AH = EmmStatus.EmmNoError;
    }
    
    public void GetPageFrameSegment() {
        // Return page frame segment in BX.
        _state.BX = EmmPageFrameSegment;
        // Set good status.
        _state.AH = EmmStatus.EmmNoError;
    }
    
    public void GetNumberOfPages() {
        // Return total number of pages in DX.
        _state.DX = (ushort) (TotalPages / 4);
        // Return number of pages available in BX.
        _state.BX = GetFreePages();
        // Set good status.
        _state.AH = EmmStatus.EmmNoError;
    }
    
    /// <summary>
    /// Allocates pages for a new handle.
    /// </summary>
    public void GetHandleAndAllocatePages() {
        ushort handles = _state.DX;
        _state.AH = EmmAllocateMemory(_state.BX, ref handles, false);
        _state.DX = handles;
    }
    
    /// <summary>
    /// Maps or unmaps a physical page.
    /// </summary>
    public void MapExpandedMemoryPage() {
        ushort handle = _state.DX;
        _state.AH = EmmMapPage(_state.BX, ref handle, _state.BX);
        _state.DX = handle;
    }
    
    /// <summary>
    /// Maps data from a logical page to a physical page.
    /// </summary>
    /// <param name="physicalPage">Physical page id.</param>
    private void MapEmmPage(int physicalPage) {
        ushort destSegment = (ushort)(EmmPageFrameSegment + EmmSegmentsPerPage * physicalPage);
        uint destAddress = MemoryUtils.ToPhysicalAddress(destSegment, 0);
        EmmMapping mapping = EmmMappings[physicalPage];
        mapping.DestAddress = destAddress;
        _memory.RegisterMapping(destAddress, mapping.Size, mapping);
    }
    
    /// <summary>
    /// Removes an EMM page mapping
    /// </summary>
    /// <param name="physicalPage">Physical page id.</param>
    private void UnmapEmmPage(int physicalPage) {
        EmmMapping mapping = EmmMappings[physicalPage];
        _memory.UnregisterMapping(mapping.DestAddress, mapping.Size, mapping);
    }
    
    private byte EmmMapPage(ushort physicalPage, ref ushort handle, ushort logicalPage) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("{@MethodName}: {@physicalPage} {@Handle}, {@LogicalPage}",
                nameof(EmmMapPage), physicalPage, handle, logicalPage);
        }
        /* Check for too high physical page */
        if (physicalPage >= EmmMappings.Length) {
            return EmmStatus.EmsIllegalPhysicalPage;
        }

        /* unmapping doesn't need valid handle (as handle isn't used) */
        if (logicalPage == EmmNullPage) {
            /* Unmapping */
            EmmMappings[physicalPage].Handle = EmmNullHandle;
            EmmMappings[physicalPage].Page = EmmNullPage;
            UnmapEmmPage(physicalPage);
            return EmmStatus.EmmNoError;
        }
        /* Check for valid handle */
        if (!IsValidHandle(handle)) {
            return EmmStatus.EmmInvalidHandle;
        }

        if (logicalPage < EmmHandles[handle].Pages) {
            /* Mapping it is */
            EmmMappings[physicalPage].Handle = handle;
            EmmMappings[physicalPage].Page = logicalPage;
            MapEmmPage(physicalPage);
            return EmmStatus.EmmNoError;
        } else {
            /* Illegal logical page it is */
            return EmmStatus.EmsLogicalPageOutOfRange;
        }
    }
    
    public bool IsValidHandle(ushort handle) {
        if (handle >= EmmHandles.Length) {
            return false;
        }
        return EmmHandles[handle].Pages != EmmNullHandle;
    }
    
    /// <summary>
    /// Deallocates a handle and all of its pages.
    /// </summary>
    public void ReleaseHandleAndFreePages() {
        _state.AH = ReleaseMemory(_state.DX);
    }

    public byte ReleaseMemory(ushort handle) {
        /* Check for valid handle */
        if (!IsValidHandle(handle)) {
            return EmmStatus.EmmInvalidHandle;
        }

        if (EmmHandles[handle].Pages != 0) {
            ReleasePages(EmmHandles[handle].MemHandle);
        }
        /* Reset handle */
        EmmHandles[handle].MemHandle = 0;
        // OS handle is NEVER deallocated
        EmmHandles[handle].Pages = handle == 0 ? (ushort) 0 : EmmNullHandle;
        EmmHandles[handle].SavePageMap = false;
        EmmHandles[handle].Name = string.Empty;
        return EmmStatus.EmmNoError;

    }

    private void ReleasePages(int handle) {
        while (handle > 0) {
            int next = MemoryBlock.MemoryHandles[handle];
            MemoryBlock.MemoryHandles[handle] = 0;
            handle = next;
        }
    }

    public void GetEmmVersion() {
        // Return EMS version 3.2.
        _state.AL = 0x32;
        // Return good status.
        _state.AH = EmmStatus.EmmNoError;
    }
    
    /// <summary>
    /// Saves the current state of page map registers for a handle.
    /// </summary>
    public void SavePageMap() {
        _state.AH = SavePageMap(_state.DX);
    }

    public byte SavePageMap(ushort handle) {
        /* Check for valid handle */
        if (handle >= EmmHandles.Length || EmmHandles[handle].Pages == EmmNullHandle) {
            if (handle != 0) {
                return EmmStatus.EmmInvalidHandle;
            }
        }
        /* Check for previous save */
        if (EmmHandles[handle].SavePageMap) {
            return EmmStatus.EmmPageMapSaved;
        }
        /* Copy the mappings over */
        for (int i = 0; i < EmmMappings.Length; i++) {
            EmmHandles[handle].PageMap[i].Page = EmmMappings[i].Page;
            EmmHandles[handle].PageMap[i].Handle = EmmMappings[i].Handle;
            EmmHandles[handle].PageMap[i].Ram = EmmMappings[i].Ram;
        }
        EmmHandles[handle].SavePageMap = true;
        return EmmStatus.EmmNoError;
    }
    
    /// <summary>
    /// Restores the state of page map registers for a handle.
    /// </summary>
    public void RestorePageMap() {
        _state.AH = RestorePageMap(_state.DX);
    }

    public byte RestorePageMap(ushort handle) {
        /* Check for valid handle */
        if (handle >= EmmHandles.Length || EmmHandles[handle].Pages == EmmNullHandle) {
            if (handle != 0) {
                return EmmStatus.EmmInvalidHandle;
            }
        }
        /* Check for previous save */
        if (!EmmHandles[handle].SavePageMap) {
            return EmmStatus.EmmNoSavedPageMap;
        }
        /* Restore the mappings */
        EmmHandles[handle].SavePageMap = false;
        for (int i = 0; i < EmmMappings.Length; i++) {
            EmmMappings[i].Page = EmmHandles[handle].PageMap[i].Page;
            EmmMappings[i].Handle = EmmHandles[handle].PageMap[i].Handle;
            EmmMappings[i].Ram = EmmHandles[handle].PageMap[i].Ram;
        }
        return RestoreMappingTable();
    }

    private byte RestoreMappingTable() {
        /* Move through the mappings table and setup mapping accordingly */
        for (int i = 0; i < 0x40; i++) {
            /* Skip the pageframe */
            if (i is >= EmmPageFrameSegment / 0x400 and < (EmmPageFrameSegment / 0x400) + EmmMaxPhysicalPages) {
                continue;
            }
            EmmMapSegment(i << 10, EmmSegmentMappings[i].Handle, EmmSegmentMappings[i].Page);
        }
        for (ushort i = 0; i < EmmMappings.Length; i++) {
            ushort handle = EmmMappings[i].Handle;
            EmmMapPage(i, ref handle, EmmMappings[i].Page);
        }
        return EmmStatus.EmmNoError;
    }

    private byte EmmMapSegment(int segment, ushort handle, ushort logicalPage) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("{@MethodName}: {@Handle}, {@Segment}, {@LogicalPage}",
                nameof(EmmMapSegment), segment, handle, logicalPage);
        }

        bool isValidSegment = false;

        if (EmsType is 1 or 3) {
            if (segment < 0xF000 + 0x1000) {
                isValidSegment = true;
            }
        } else {
            switch (segment) {
                case >= 0xa000 and < 0xb000:
                // allow mapping of EMS page frame
                case >= EmmPageFrameSegment and < EmmPageFrameSegment + 0x1000:
                    // allow mapping of graphics memory
                    isValidSegment = true;
                    break;
            }
        }

        if (!isValidSegment) {
            return EmmStatus.EmsIllegalPhysicalPage;
        }

        int physicalPage = (segment - EmmPageFrameSegment) / (0x1000 / EmmMappings.Length);

        /* unmapping doesn't need valid handle (as handle isn't used) */
        if (logicalPage == EmmNullPage) {
            /* Unmapping */
            if (physicalPage is >= 0 and < EmmMaxPhysicalPages) {
                EmmMappings[physicalPage].Handle = EmmNullHandle;
                EmmMappings[physicalPage].Page = EmmNullPage;
                UnmapEmmPage(physicalPage);
            } else {
                EmmSegmentMappings[segment >> 10].Handle = EmmNullHandle;
                EmmSegmentMappings[segment >> 10].Page = EmmNullPage;
                UnmapEmmPage(segment >> 10);
            }
            return EmmStatus.EmmNoError;
        }
        if (!IsValidHandle(handle)) {
            return EmmStatus.EmmInvalidHandle;
        }

        if (logicalPage >= EmmHandles[handle].Pages) {
            return EmmStatus.EmsLogicalPageOutOfRange;
        }

        // Mapping
        if (physicalPage is >= 0 and < EmmMaxPhysicalPages) {
            EmmMappings[physicalPage].Handle = handle;
            EmmMappings[physicalPage].Page = logicalPage;
            MapEmmPage(physicalPage);
        } else {
            EmmSegmentMappings[segment >> 10].Handle = handle;
            EmmSegmentMappings[segment >> 10].Page = logicalPage;
            MapEmmPage(segment >> 10);
        }

        return EmmStatus.EmmNoError;
    }

    public void GetHandleCount() {
        _state.BX = 0;
        _state.BX = CalculateHandleCount();
        // Return good status.
        _state.AH = EmmStatus.EmmNoError;
    }

    /// <summary>
    /// Returns the number of EMM handles
    /// </summary>
    /// <returns>The number of EMM handles</returns>
    private ushort CalculateHandleCount() {
        ushort count = 0;
        foreach (EmmHandle handle in EmmHandles)
        {
            if (handle.Pages != EmmNullHandle) {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Gets the number of pages allocated to a handle.
    /// </summary>
    public void GetPagesForOneHandle() {
        if (!IsValidHandle(_state.DX)) {
            _state.AH = EmmStatus.EmmInvalidHandle;
            return;
        }

        _state.BX = GetPagesForOneHandle(_state.DX);
        _state.AH = EmmStatus.EmmNoError;
    }

    public ushort GetPagesForOneHandle(ushort handle) {
        return EmmHandles[handle].Pages;
    }
    
    private void GetPageForAllHandles() {
        ushort handles = _state.BX;
        _state.AH = GetPagesForAllHandles(MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI), ref handles);
        _state.BX = handles;
    }

    private byte GetPagesForAllHandles(uint table, ref ushort handles) {
        handles = 0;
        for (byte i = 0; i < EmmMaxHandles; i++) {
            if (EmmHandles[i].Pages == EmmNullHandle) {
                continue;
            }
            handles++;
            _memory.SetUint8(table, i);
            _memory.SetUint16(table, handles);
        }
        return EmmStatus.EmmNoError;
    }

    private void SaveOrRestorePageMap() {
        SaveOrRestorePageMap(_state.AL);
    }

    /// <summary>
    /// Saves or restore the page map
    /// </summary>
    /// <param name="operation">0: Save, 1: Restore, 2: Save and Restore, 3: Get Map Page Array Size</param>
    public void SaveOrRestorePageMap(byte operation) {
        switch (operation) {
            case 0x00:	/* Save Page Map */
            uint physicalAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI);
            // TODO: Remove this, use Span
            foreach (EmmMapping mapping in EmmMappings) {
                _memory.SetUint16(physicalAddress, mapping.Handle);
            }
            _state.AH = EmmStatus.EmmNoError;
            break;
            case 0x01:	/* Restore Page Map */
                uint address = MemoryUtils.ToPhysicalAddress(_state.DS, _state.ES);
                // TODO: Remove this, use Span
                for (int i = 0; i < EmmMappings.Length; i++) {
                    EmmMappings[i].Handle = _memory.GetUint16((uint) (address + i));
                    EmmMappings[i].Page = _memory.GetUint16((uint) (address + i + sizeof(ushort)));
                    address += (ushort)(i + sizeof(ushort));
                }
                _state.AH = EmmRestoreMappingTable();
                break;
            case 0x02:	/* Save and Restore Page Map */
                uint offset = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI);
                // TODO: Remove this, use Span
                for (int i = 0; i < EmmMappings.Length; i++) {
                    EmmMapping item = EmmMappings[i];
                    _memory.LoadData((uint)(offset + i * EmmMappings.Length),
                        BitConverter.GetBytes(item.Handle).Union(BitConverter.GetBytes(item.Page))
                            .ToArray());
                }
                offset = MemoryUtils.ToPhysicalAddress(_state.DS, _state.ES);
                for (int i = 0; i < EmmMappings.Length; i++) {
                    EmmMappings[i].Handle = _memory.GetUint16((uint) (offset + i));
                    EmmMappings[i].Page = _memory.GetUint16((uint) (offset + i + sizeof(ushort)));
                    offset += (ushort)(i + sizeof(ushort));
                }
                _state.AH = EmmRestoreMappingTable();
            break;
            case 0x03:	/* Get Page Map Array Size */
                _state.AL = (byte)EmmMappings.Length;
                _state.AH = EmmStatus.EmmNoError;
            break;
            default:
            if (_loggerService.IsEnabled(LogEventLevel.Error))
            {
                _loggerService.Error(
                    "{@MethodName} subFunction number {@SubFunctionId} not supported",
                    nameof(SaveOrRestorePageMap), operation);
            }
            _state.AH = EmmStatus.EmmInvalidSubFunction;
            break;
        }
    }

    private byte EmmRestoreMappingTable() {
        /* Move through the mappings table and setup mapping accordingly */
        for (int i = 0; i < 0x40; i++) {
            /* Skip the pageframe */
            if (i is >= EmmPageFrameSegment / 0x400 and < (EmmPageFrameSegment / 0x400) + EmmMaxPhysicalPages) continue;
            EmmMapSegment(i << 10, EmmSegmentMappings[i].Handle, EmmSegmentMappings[i].Page);
        }
        for (byte i = 0; i < EmmMaxPhysicalPages; i++) {
            ushort handle = EmmMappings[i].Handle;
            EmmMapPage(i, ref handle, EmmMappings[i].Page);
            EmmMappings[i].Handle = handle;
        }
        return EmmStatus.EmmNoError;
    }
    
    private void SaveOrRestorePartialPageMap() {
        _state.AH = SaveOrRestorePartialPageMap(_state.AL);
    }

    /// <summary>
    /// Save or restore partial page map
    /// </summary>
    /// <param name="operation">0: Save partial page map, 1: Restore partial page map, 2: Get partial page map array size</param>
    /// <returns></returns>
    public byte SaveOrRestorePartialPageMap(byte operation) {
        ushort count;
        uint data;
        switch (operation) {
            case 0x00:    /* Save Partial Page Map */
                uint list = MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI);
                data = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI);
                count = _memory.GetUint16(list);
                list += 2;
                _memory.SetUint16(data, count);
                data += 2;
                for (; count > 0; count--) {
                    ushort segment = _memory.GetUint16(list);
                    list += 2;
                    if (segment is >= EmmPageFrameSegment and < EmmPageFrameSegment + 0x1000) {
                        ushort page = (ushort) ((segment - EmmPageFrameSegment) / (EmmPageFrameSegment >> 4));
                        _memory.SetUint16(data, segment);
                        data += 2;
                        _memory.LoadData(
                            data, BitConverter.GetBytes(EmmMappings[page].Handle).Union(BitConverter.GetBytes(EmmMappings[page].Page)).ToArray(), EmmMappings.Length);
                        data += (uint)EmmMappings.Length;
                    } else if (EmsType is 1 or 3 || segment is >= EmmPageFrameSegment - 0x1000 and < EmmPageFrameSegment or >= 0xa000 and < 0xb000) {
                        _memory.SetUint16(data, segment);
                        data += 2;
                        _memory.LoadData(
                            data, BitConverter.GetBytes(EmmSegmentMappings[segment >> 10].Handle).Union(BitConverter.GetBytes(EmmSegmentMappings[segment >> 10].Page)).ToArray(), EmmSegmentMappings.Length);
                        data += (uint)EmmMappings.Length;
                    } else {
                        return EmmStatus.EmsIllegalPhysicalPage;
                    }
                }
                break;
            case 0x01:    /* Restore Partial Page Map */
                data = MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI);
                count = _memory.GetUint16(data);
                data += 2;
                for (; count > 0; count--) {
                    ushort segment = _memory.GetUint16(data);
                    data += 2;
                    if (segment is >= EmmPageFrameSegment and < EmmPageFrameSegment + 0x1000) {
                        ushort page = (ushort) ((segment - EmmPageFrameSegment) / (EmmPageSize >> 4));
                        EmmMappings[page].Handle = _memory.GetUint16(data);
                        EmmMappings[page].Page = _memory.GetUint16(data + sizeof(ushort));
                    } else if (EmsType is 1 or 3 || segment is >= EmmPageFrameSegment - 0x1000 and < EmmPageFrameSegment or >= 0xa000 and < 0xb000) {
                        EmmSegmentMappings[segment >> 10].Handle = _memory.GetUint16(data);
                        EmmSegmentMappings[segment >> 10].Page = _memory.GetUint16(data + sizeof(ushort));
                    } else {
                        return EmmStatus.EmsIllegalPhysicalPage;
                    }
                    data += (uint)EmmMappings.Length;
                }
                return EmmRestoreMappingTable();
            case 0x02:    /* Get Partial Page Map Array Size */
                _state.AL = (byte)(2 + _state.BX * 2 * EmmMappings.Length);
                break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Error))
                {
                    _loggerService.Error(
                        "{@MethodName} subFunction number {@SubFunctionId} not supported",
                        nameof(SaveOrRestorePartialPageMap), operation);
                }
                return EmmStatus.EmmInvalidSubFunction;
        }
        return EmmStatus.EmmNoError;
    }
    
    public void MapOrUnmapMultipleHandlePages() {
        ushort operation = _state.AL;
        _state.AH = EmmStatus.EmmNoError;
        MapOrUnmapMultipleHandlePages(operation);
    }

    /// <summary>
    /// Map or unmap multiple handle pages
    /// </summary>
    /// <param name="operation">0: Use physical page numbers, 1: Use segment addressing</param>
    public void MapOrUnmapMultipleHandlePages(ushort operation)
    {
        switch (operation)
        {
            case 0x00: // use physical page numbers
                uint dataPhysical = MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI);
                for (int i = 0; i < _state.CX; i++)
                {
                    ushort logPage = _memory.GetUint16(dataPhysical);
                    dataPhysical += 2;
                    ushort physPage = _memory.GetUint16(dataPhysical);
                    dataPhysical += 2;
                    ushort handle = _state.DX;
                    _state.AH = EmmMapPage(physPage, ref handle, logPage);
                    if (_state.AH != EmmStatus.EmmNoError)
                    {
                        break;
                    }
                }

                break;
            case 0x01: // use segment address
            {
                uint data = MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI);
                for (int i = 0; i < _state.CX; i++)
                {
                    ushort logPage = _memory.GetUint16(data);
                    data += 2;
                    _state.AH = EmmMapSegment(_memory.GetUint16(data), _state.DX, logPage);
                    data += 2;
                    if (_state.AH != EmmStatus.EmmNoError)
                    {
                        break;
                    }
                }
            }
                break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Error))
                {
                    _loggerService.Error(
                        "{@MethodName} subFunction number {@SubFunctionId} not supported",
                        nameof(MapOrUnmapMultipleHandlePages), operation);
                }
                _state.AH = EmmStatus.EmmInvalidSubFunction;
                break;
        }
    }

    /// <summary>
    /// Reallocates pages for a handle.
    /// </summary>
    public void ReallocatePages() {
        _state.AH = ReallocatePages(_state.DX, _state.BX);
    }

    public byte ReallocatePages(ushort handle, ushort pages) {
        /* Check for valid handle */
        if (!IsValidHandle(handle)) {
            return EmmStatus.EmmInvalidHandle;
        }
        if (EmmHandles[handle].Pages != 0) {
            /* Check for enough pages */
            int mem = EmmHandles[handle].MemHandle;
            if (!ReallocatePages(ref mem, (ushort) (pages * 4), false)) {
                return EmmStatus.EmmOutOfLogicalPages;
            }
            EmmHandles[handle].MemHandle = mem;
        } else {
            int mem = AllocatePages((ushort) (pages * 4), false);
            if (mem == 0) {
                FailFastWithLogMessage("EMS:Memory allocation failure during reallocation");
            }
            EmmHandles[handle].MemHandle = mem;
        }
        /* Update size */
        EmmHandles[handle].Pages = pages;
        return EmmStatus.EmmNoError;
    }

    private bool ReallocatePages(ref int handle, ushort pages, bool sequence) {
        if (handle <= 0) {
            if (pages == 0) {
                return true;
            }
            handle = AllocatePages(pages, sequence);
            return (handle > 0);
        }
        if (pages == 0) {
            ReleasePages(handle);
            handle = -1;
            return true;
        }
        int index = handle;
        int last = 0;
        ushort oldPages = 0;
        while (index > 0) {
            oldPages++;
            last = index;
            index = MemoryBlock.MemoryHandles[index];
        }

        if (oldPages == pages) {
            return true;
        }
        if (oldPages > pages) {
            /* Decrease size */
            pages--;
            index = handle;
            oldPages--;
            while (pages != 0) {
                index = MemoryBlock.MemoryHandles[index];
                pages--;
                oldPages--;
            }
            int next = MemoryBlock.MemoryHandles[index];
            MemoryBlock.MemoryHandles[index] = -1;
            index = next;
            while (oldPages != 0) {
                next = MemoryBlock.MemoryHandles[index];
                MemoryBlock.MemoryHandles[index] = 0;
                index = next;
                oldPages--;
            }
            return true;
        } else {
            /* Increase size, check for enough free space */
            ushort need = (ushort) (pages - oldPages);
            if (sequence) {
                index = last + 1;
                int free = 0;
                while (index < MemoryBlock.Pages && MemoryBlock.MemoryHandles[index] == 0) {
                    index++;
                    free++;
                }
                if (free >= need) {
                    /* Enough space, allocate more pages */
                    index = last;
                    while (need != 0) {
                        MemoryBlock.MemoryHandles[index] = index + 1;
                        need--;
                        index++;
                    }
                    MemoryBlock.MemoryHandles[index] = -1;
                    return true;
                } else {
                    /* Not Enough space, allocate new block and copy */
                    int newHandle = AllocatePages(pages, true);
                    if (newHandle == 0) {
                        return false;
                    }
                    _memory.MemCopy((uint) (newHandle * 4096), (uint) (handle * 4096), (uint) (oldPages * 4096));
                    ReleasePages(handle);
                    handle = newHandle;
                    return true;
                }
            } else {
                int rem = AllocatePages(need, false);
                if (rem == 0) {
                    return false;
                }
                MemoryBlock.MemoryHandles[last] = rem;
                return true;
            }
        }
    }

    public void SetGetHandleName() {
        ushort handle = _state.DX;
        _state.AH = GetSetHandleName(handle, _state.AL);
    }

    /// <summary>
    /// Gets or Set a handle name, depending on the <paramref name="operation"/>
    /// </summary>
    /// <param name="handle">The handle reference</param>
    /// <param name="operation">Get: 0, Set: 1</param>
    /// <returns>The state of the operation</returns>
    public byte GetSetHandleName(ushort handle, byte operation)
    {
        switch (operation)
        {
            case EmsSubFunctions.HandleNameGet:
                if (handle >= EmmHandles.Length || EmmHandles[handle].Pages == EmmNullHandle) {
                    return EmmStatus.EmmInvalidHandle;
                }
                GetHandleName(handle);
                break;

            case EmsSubFunctions.HandleNameSet:
                if (handle >= EmmHandles.Length || EmmHandles[handle].Pages == EmmNullHandle) {
                    return EmmStatus.EmmInvalidHandle;
                }
                SetHandleName(handle,
                    MemoryUtils.GetZeroTerminatedString(_memory.Ram, MemoryUtils.ToPhysicalAddress(_state.SI, _state.DI),
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

    private void HandleFunctions() {
        _state.AH = HandleFunctions(_state.AL);
    }

    /// <summary>
    /// Handle Functions.
    /// </summary>
    /// <param name="operation">0: Get all handle names. 1: Search for a handle name. 2: Get total number of handles. Other values: invalid subFunction.s</param>
    public byte HandleFunctions(byte operation) {
        string name;
        ushort handle = 0;
        uint data;
        switch (operation) {
            case 0x00:    /* Get all handle names */
                _state.AL = 0;
                data = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI);
                for (handle = 0; handle < EmmMaxHandles; handle++) {
                    if (EmmHandles[handle].Pages == EmmNullHandle) {
                        continue;
                    }
                    _state.AL++;
                    _memory.SetUint16(data, handle);
                    _memory.LoadData(data + 2, Encoding.ASCII.GetBytes(EmmHandles[handle].Name), 8);
                    data += 10;
                }
                break;
            case 0x01: /* Search for a handle name */
                name = MemoryUtils.GetZeroTerminatedString(_memory.Ram,
                    MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI), 8);
                for (handle = 0; handle < EmmMaxHandles; handle++) {
                    if (EmmHandles[handle].Pages == EmmNullHandle ||
                        !name.Equals(EmmHandles[handle].Name, StringComparison.InvariantCultureIgnoreCase)) {
                        continue;
                    }

                    _state.DX = handle;
                    return EmmStatus.EmmNoError;
                }
                return EmmStatus.EmmNotFound;
            case 0x02: /* Get Total number of handles */
                _state.BX = (ushort) EmmHandles.Length;
                break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("{@MethodName}: EMS subfunction number {@SubFunction} not implemented",
                        nameof(HandleFunctions), operation);
                }
                return EmmStatus.EmmInvalidSubFunction;
        }
        return EmmStatus.EmmNoError;
    }

    public void GetMappablePhysicalArrayAddressArray() {
        _state.AH = GetMappablePhysicalArrayAddressArray(_state.AL);
    }

    /// <summary>
    /// Get mappable physical array address array
    /// </summary>
    /// <param name="operation">0: Get the information. Other value: Invalid subFunction.</param>
    public byte GetMappablePhysicalArrayAddressArray(byte operation) {
        switch (operation) {
            case 0x00: {
                uint data = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI);
                int step = 0x1000 / EmmMappings.Length;
                for (int i = 0; i < EmmMappings.Length; i++) {
                    _memory.SetUint16(data, (ushort) (EmmPageFrameSegment + step * i));
                    data += 2;
                    _memory.SetUint16(data, (ushort) i);
                    data += 2;
                }
                break;
            }
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("{@MethodName}: EMS subfunction number {@SubFunction} not implemented",
                        nameof(GetMappablePhysicalArrayAddressArray), operation);
                }
                return EmmStatus.EmmInvalidSubFunction;
        }
        // Set number of pages
        _state.CX = (ushort) EmmMappings.Length;
        return EmmStatus.EmmNoError;
    }

    public void GetHardwareInformation() {
        switch (_state.AL) {
            case EmsSubFunctions.GetHardwareConfiguration:
                uint data = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI);
                // 1 page is 1K paragraphs (16KB)
                _memory.SetUint16(data,0x0400);
                data+=2;
                // No alternate register sets
                _memory.SetUint16(data,0x0000);
                data+=2;
                // Context save area size
                _memory.SetUint16(data, (ushort)EmmMappings.Length);
                data+=2;
                // No DMA channels
                _memory.SetUint16(data,0x0000);
                data+=2;
                // Always 0 for LIM standard
                _memory.SetUint16(data,0x0000);
                break;
            case EmsSubFunctions.GetHardwareInformationUnallocatedRawPages:
                // Return number of pages available in BX.
                _state.BX = GetFreePages();
                // Return total number of pages in DX.
                _state.DX = (ushort) TotalPages;
                // Set good status.
                _state.AH = EmmStatus.EmmNoError;
            break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("{@MethodName}: EMS subfunction number {@SubFunction} not implemented",
                        nameof(GetHardwareInformation), _state.AL);
                }
                break;
        }
    }

    public void AllocateStandardRawPages() {
        _state.AH = AllocateStandardRawPages(_state.AL);
    }

    /// <summary>
    /// Allocate standard raw pages
    /// </summary>
    /// <param name="operation">less than 1: allocate page, even with 0 length. Other values: Invalid subFunction</param>
    public byte AllocateStandardRawPages(byte operation) {
        switch (operation) {
            case <= 0x01: {
                ushort handle = _state.DX;
                EmmAllocateMemory(_state.BX, ref handle, true);
                _state.DX = handle;
                break;
            }
            default: {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("{@MethodName}: EMS subfunction number {@SubFunction} not implemented",
                        nameof(AllocateStandardRawPages), operation);
                }
                return EmmStatus.EmmInvalidSubFunction;
            }
        }
        return EmmStatus.EmmNoError;
    }

    public byte EmmAllocateMemory(ushort pages, ref ushort dhandle, bool canAllocateZeroPages) {
        // Check for 0 page allocation
        if (pages is 0 && !canAllocateZeroPages) {
            return EmmStatus.EmmZeroPages;
        }
        
        // Check for enough free pages
        if (GetFreeMemoryTotal() / 4 < pages) {
            return EmmStatus.EmmOutOfLogicalPages;
        }

        ushort handle = 1;
        // Check for a free handle
        while (EmmHandles[handle].Pages != EmmNullHandle) {
            if (++handle >= EmmHandles.Length) {
                return EmmStatus.EmmOutOfHandles;
            }
        }

        if (pages == 0) {
            return EmmStatus.EmmNoError;
        }

        int memHandle = AllocatePages((ushort)(pages * 4), false);
        if (memHandle == 0) {
            throw new UnrecoverableException("EMS: Memory allocation failure");
        }

        EmmHandles[handle].Pages = pages;
        EmmHandles[handle].MemHandle = memHandle;
        // Change handle only if there is no error.
        dhandle = handle;
        return EmmStatus.EmmNoError;
    }

    /// <summary>
    /// Allocates an EMS Memory page, or several.
    /// </summary>
    /// <param name="pages">The number of pages the allocated memory page must at least have.</param>
    /// <param name="sequence">Whether to allocate in sequence or not.</param>
    /// <returns></returns>
    private int AllocatePages(ushort pages, bool sequence) {
        try {
            int ret = -1;
            if (pages == 0) {
                return 0;
            }
            if (sequence) {
                int index = BestMatch(pages);
                if (index == 0) {
                    return 0;
                }
                while (pages != 0) {
                    if (ret == -1) {
                        ret = index;
                    } else {
                        MemoryBlock.MemoryHandles[index - 1] = index;
                    }
                    index++;
                    pages--;
                }
                MemoryBlock.MemoryHandles[index - 1] = -1;
            } else {
                if (GetFreeMemoryTotal() < pages) {
                    return 0;
                }
                int lastIndex = -1;
                while (pages != 0) {
                    int index = BestMatch(1);
                    if (index == 0) {
                        FailFastWithLogMessage($"EMS: Memory corruption in {nameof(AllocatePages)}");
                    }
                    while (pages != 0 && (MemoryBlock.MemoryHandles[index] == 0)) {
                        if (ret == -1) {
                            ret = index;
                        } else {
                            MemoryBlock.MemoryHandles[lastIndex] = index;
                        }
                        lastIndex = index;
                        index++;
                        pages--;
                    }
                    // Invalidate it in case we need another match.
                    MemoryBlock.MemoryHandles[lastIndex] = -1;
                }
            }
            return ret;
        } catch (ArgumentOutOfRangeException e) {
            throw new UnrecoverableException("EMS: Memory allocation failure", e);
        }
    }

    /// <summary>
    /// Returns the EMS memory page ID with the most appropriate length
    /// </summary>
    /// <param name="requestedSize">The requested memory block size</param>
    /// <returns>The index of the first memory page that is greater than requestedSize</returns>
    private int BestMatch(int requestedSize) {
        int index = 0;
        int first = 0;
        int best = 0xfffffff;
        int bestMatch = 0;
        while (index < MemoryBlock.Pages) {
            /* Check if we are searching for first free page */
            if (first == 0) {
                /* Check if this is a free page */
                if (MemoryBlock.MemoryHandles[index] == 0) {
                    first = index;
                }
            } else {
                /* Check if this still is used page */
                if (MemoryBlock.MemoryHandles[index] != 0) {
                    int pages = index - first;
                    if (pages == requestedSize) {
                        return first;
                    }
                    if (pages > requestedSize && pages < best) {
                        best = pages;
                        bestMatch = first;
                    }
                    // Always reset for new search
                    first = 0;
                }
            }
            index++;
        }
        /* Check for the final block if we can */
        if (first != 0 && index - first >= requestedSize && index - first < best) {
            return first;
        }
        return bestMatch;
    }

    [DoesNotReturn]
    private void FailFastWithLogMessage(string message, [CallerMemberName] string methodName = nameof(FailFastWithLogMessage)) {
        UnrecoverableException e = new(message);
        if(_loggerService.IsEnabled(LogEventLevel.Fatal)) {
            _loggerService.Fatal(e, " \"Fatal error in {@MethodName} {@ExceptionMessage}\"", methodName, e.Message);
        }
        throw e;
    }

    /// <summary>
    /// Gets the name of a handle.
    /// </summary>
    public string GetHandleName(ushort handle) {
        MemoryUtils.SetZeroTerminatedString(
            _memory.Ram, MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI), EmmHandles[handle].Name, 8);
        return EmmHandles[handle].Name;
    }

    /// <summary>
    /// Set the name of a handle.
    /// </summary>
    public void SetHandleName(ushort handle, string name) {
        EmmHandles[handle].Name = name;
    }
}