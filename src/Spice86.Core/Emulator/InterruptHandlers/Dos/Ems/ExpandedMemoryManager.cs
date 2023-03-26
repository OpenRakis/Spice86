namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

/// <summary>
/// Provides DOS applications with EMS memory.
/// </summary>
public sealed class ExpandedMemoryManager : InterruptHandler {
    public const ushort PageFrameSegment = 0xE000;

    public const string EmsIdentifier = "EMMXXXX0";

    public const ushort EmmMaxHandles = 200;

    public const byte EmmMaxPhysPage = 4;

    public const ushort EmmNullPage = 0xFFFF;
    
    public const ushort EmmNullHandle = 0xFFFF;

    public const ushort EmmPageFrame = 0xE000;
    
    public override ushort? InterruptHandlerSegment => 0xF100;
    
    public override byte Index => 0x67;

    private readonly ILoggerService _loggerService;
    
    public MemoryBlock MemoryBlock { get; }

    public EmmMapping[] EmmSegmentMappings { get; } = new EmmMapping[0x40];

    public EmmMapping[] EmmMappings { get; } = new EmmMapping[EmsHandle.EmmMaxPhysicalPages];
    
    public EmsHandle[] EmmHandles { get; } = new EmsHandle[EmmMaxHandles];
    
    public const ushort XmsStart = 0x110;

    public const int MemorySizeInMb = 6;

    public int TotalPages => MemoryBlock.Pages;

    public ushort GetFreeMemoryTotal() {
        ushort free = 0;
        ushort index = XmsStart;
        while (index < TotalPages) {
            if (MemoryBlock.MemoryHandles[index] == 0) {
                free++;
            }
            index++;
        }
        return free;
    }

    public ushort GetFreePages() => Math.Min((ushort) 0x7FFF, (ushort) (GetFreeMemoryTotal() / 4));

    public ExpandedMemoryManager(Machine machine, ILoggerService loggerService) : base(machine) {
        _loggerService = loggerService;
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
        _dispatchTable.Add(0x50, new Callback(0x50, MapOrUnmapMultiplePageMap));
        _dispatchTable.Add(0x51, new Callback(0x51, ReallocatePages));
        _dispatchTable.Add(0x53, new Callback(0x53, SetGetHandleName));
        _dispatchTable.Add(0x54, new Callback(0x54, HandleFunctions));
        _dispatchTable.Add(0x57, new Callback(0x57, MemoryRegion));
        _dispatchTable.Add(0x58, new Callback(0x58, GetMappablePhysicalArrayAddressArray));
        _dispatchTable.Add(0x59, new Callback(0x59, GetHardwareInformation));
        _dispatchTable.Add(0x5A, new Callback(0x5A, AllocateStandardRawPages));
    }
    
    public void GetStatus() {
        // Return good status in AH.
        _state.AH = EmsStatus.EmmNoError;
    }
    
    public void GetPageFrameSegment() {
        // Return page frame segment in BX.
        _state.BX = PageFrameSegment;
        // Set good status.
        _state.AH = EmsStatus.EmmNoError;
    }
    
    public void GetNumberOfPages() {
        // Return total number of pages in DX.
        _state.DX = (ushort) (TotalPages / 4);
        // Return number of pages available in BX.
        _state.BX = GetFreePages();
        // Set good status.
        _state.AH = EmsStatus.EmmNoError;
    }
    
    /// <summary>
    /// Allocates pages for a new handle.
    /// </summary>
    public void GetHandleAndAllocatePages() {
        ushort handles = _state.DX;
        _state.AX = EmmAllocateMemory(_state.BX, ref handles, false);
        _state.DX = handles;
    }
    
    /// <summary>
    /// Maps or unmaps a physical page.
    /// </summary>
    public void MapExpandedMemoryPage() {
        ushort handle = _state.DX;
        _state.AX = EmmMapPage(_state.AX, ref handle, _state.BX);
        _state.DX = handle;
    }
    
    private byte EmmMapPage(ushort physicalPage, ref ushort handle, ushort logicalPage) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("{@MethodName}: {@physicalPage} {@Handle}, {@LogicalPage}",
                nameof(EmmMapPage), physicalPage, handle, logicalPage);
        }
        /* Check for too high physical page */
        if (physicalPage >= EmmMaxPhysPage) {
            return EmsStatus.EmsIllegalPhysicalPage;
        }

        /* unmapping doesn't need valid handle (as handle isn't used) */
        if (logicalPage == EmmNullPage) {
            /* Unmapping */
            EmmMappings[physicalPage].Handle = EmmNullHandle;
            EmmMappings[physicalPage].Page = EmmNullPage;
            return EmsStatus.EmmNoError;
        }
        /* Check for valid handle */
        if (!IsValidHandle(handle)) {
            return EmsStatus.EmmInvalidHandle;
        }

        if (logicalPage < EmmHandles[handle].Pages) {
            /* Mapping it is */
            EmmMappings[physicalPage].Handle = handle;
            EmmMappings[physicalPage].Page = logicalPage;
            return EmsStatus.EmmNoError;
        } else {
            /* Illegal logical page it is */
            return EmsStatus.EmsLogicalPageOutOfRange;
        }
    }
    
    public bool IsValidHandle(ushort handle) {
        if (handle >= EmmMaxHandles) {
            return false;
        }
        return EmmHandles[handle].Pages != EmmNullHandle;
    }
    
    /// <summary>
    /// Deallocates a handle and all of its pages.
    /// </summary>
    public void ReleaseHandleAndFreePages() {
        int handle = _state.DX;
    }

    public void GetEmmVersion() {
        // Return EMS version 4.0.
        _state.AL = 0x40;
        // Return good status.
        _state.AH = EmsStatus.EmmNoError;
    }
    
    /// <summary>
    /// Saves the current state of page map registers for a handle.
    /// </summary>
    public void SavePageMap() {
        _state.AX = SavePageMap(_state.DX);
    }

    public ushort SavePageMap(ushort handle) {
        /* Check for valid handle */
        if (handle >= EmmMaxHandles || EmmHandles[handle].Pages == EmmNullHandle) {
            if (handle != 0) {
                return EmsStatus.EmmInvalidHandle;
            }
        }
        /* Check for previous save */
        if (EmmHandles[handle].SavePagedMap) {
            return EmsStatus.EmmPageMapSaved;
        }
        /* Copy the mappings over */
        for (int i = 0; i < EmmMaxPhysPage; i++) {
            EmmHandles[handle].PageMap[i].Page = EmmMappings[i].Page;
            EmmHandles[handle].PageMap[i].Handle = EmmMappings[i].Handle;
        }
        EmmHandles[handle].SavePagedMap = true;
        return EmsStatus.EmmNoError;
    }
    
    /// <summary>
    /// Restores the state of page map registers for a handle.
    /// </summary>
    public void RestorePageMap() {
    }
    
    public void GetHandleCount() {
        // Return the number of EMM handles (plus 1 for the OS handle).
        _state.BX = (ushort)(EmmHandles.Length + 1);
        // Return good status.
        _state.AH = EmsStatus.EmmNoError;
    }
    
    /// <summary>
    /// Gets the number of pages allocated to a handle.
    /// </summary>
    public void GetPagesForOneHandle() {
        int handleIndex = _state.DX;
    }
    
    private void GetPageForAllHandles() {
        throw new NotImplementedException();
    }
    
    private void SaveOrRestorePageMap() {
        throw new NotImplementedException();
    }
    
    private void SaveOrRestorePartialPageMap() {
        throw new NotImplementedException();
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

    /// <summary>
    /// Reallocates pages for a handle.
    /// </summary>
    public void ReallocatePages() {
        
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

    private void HandleFunctions() {
        throw new NotImplementedException();
    }

    public void MemoryRegion() {
        switch (_state.AL) {
            case EmsSubFunctions.MoveExchangeMove:
                //Move();
                break;

            default:
                throw new NotImplementedException($"EMM function 57{_state.AL:X2}h not implemented.");
        }
    }

    private void GetMappablePhysicalArrayAddressArray() {
        throw new NotImplementedException();
    }
    
    public void GetHardwareInformation() {
        switch (_state.AL) {
        case EmsSubFunctions.GetHardwareInformationUnallocatedRawPages:
            // Return number of pages available in BX.
            _state.BX = GetFreePages();
            // Return total number of pages in DX.
            _state.DX = (ushort) TotalPages;
            // Set good status.
            _state.AH = EmsStatus.EmmNoError;
            break;

        default:
            throw new UnrecoverableException();
        }
    }

    private void AllocateStandardRawPages() {
        throw new NotImplementedException();
    }
    
    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    public ushort EmmAllocateMemory(ushort pages, ref ushort dhandle, bool canAllocateZeroPages) {
        // Check for 0 page allocation
        if (pages is 0 && !canAllocateZeroPages) {
            return EmsStatus.EmmZeroPages;
        }
        
        // Check for enough free pages
        if (GetFreeMemoryTotal() / 4 < pages) {
            return EmsStatus.EmmOutOfLogicalPages;
        }

        ushort handle = 1;
        // Check for a free handle
        while (EmmHandles[handle].Pages > 0) {
            if (++handle >= EmmMaxHandles) {
                return EmsStatus.EmmOutOfHandles;
            }
        }

        int mem;
        if (pages != 0) {
            mem = AllocatePages((ushort)(pages * 4), false);
            if (mem == 0) {
                throw new UnrecoverableException("EMS: Memory allocation failure");
            }

            EmmHandles[handle].Pages = pages;
            EmmHandles[handle].MemHandle = mem;
            // Change handle only if there is no error.
            dhandle = handle;
        }
        return EmsStatus.EmmNoError;
    }

    /// <summary>
    /// TODO: Merge this with <see cref="DosMemoryManager"/>
    /// </summary>
    /// <param name="pages">The number of pages the allocated memory page must at least have.</param>
    /// <param name="sequence">Whether to allocate in sequence or not.</param>
    /// <returns></returns>
    private int AllocatePages(ushort pages, bool sequence) {
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
                if (ret == -1)
                    ret = index;
                else {
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
    }

    /// <summary>
    /// TODO: Merge this with <see cref="DosMemoryManager"/>
    /// </summary>
    /// <param name="requestedSize">The requested memory block size</param>
    /// <returns>The index of the first memory page that is greater than requestedSize</returns>
    private int BestMatch(int requestedSize) {
        int index = XmsStart;
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
                    } else if (pages > requestedSize) {
                        if (pages < best) {
                            best = pages;
                            bestMatch = first;
                        }
                    }
                    // Always reset for new search
                    first = 0;
                }
            }
            index++;
        }
        /* Check for the final block if we can */
        if (first != 0 && (index - first >= requestedSize) && (index - first < best)) {
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
    public void GetHandleName() {
    }

    /// <summary>
    /// Set the name of a handle.
    /// </summary>
    public void SetHandleName() {
    }

    /// <summary>
    /// Maps or unmaps multiple pages.
    /// </summary>
    public void MapUnmapMultiplePages() {
    }
}
