namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Serilog.Events;

using System.Numerics;
using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.Memory;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

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

    private ILoggerService _loggerService;

    public ExpandedMemoryManager(Machine machine, ILoggerService loggerService) : base(machine) {
        _loggerService = loggerService;
        var device = new CharacterDevice(DeviceAttributes.Ioctl, EmsIdentifier);
        machine.Dos.AddDevice(device, InterruptHandlerSegment, 0x0000);
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
        _state.AH = EmsErrors.EmmNoError;
    }
    
    public void GetPageFrameSegment() {
        // Return page frame segment in BX.
        _state.BX = PageFrameSegment;
        // Set good status.
        _state.AH = EmsErrors.EmmNoError;
    }
    
    public void GetNumberOfPages() {
        // Return total number of pages in DX.
        _state.DX = (ushort) (_machine.EmsCard.TotalPages / 4);
        // Return number of pages available in BX.
        _state.BX = _machine.EmsCard.FreePages;
        // Set good status.
        _state.AH = EmsErrors.EmmNoError;
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
        EmmMapPage(_state.AX, ref handle, _state.BX);
        _state.DX = handle;
    }
    
    private byte EmmMapPage(ushort physicalPage, ref ushort handle, ushort logicalPage) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("{@MethodName}: {@physicalPage} {@Handle}, {@LogicalPage}",
                nameof(EmmMapPage), physicalPage, handle, logicalPage);
        }
        /* Check for too high physical page */
        if (physicalPage >= EmmMaxPhysPage) {
            return EmsErrors.EmsIllegalPhysicalPage;
        }

        /* unmapping doesn't need valid handle (as handle isn't used) */
        if (logicalPage == EmmNullPage) {
            /* Unmapping */
            _machine.EmsCard.EmmMappings[physicalPage].Handle = EmmNullHandle;
            _machine.EmsCard.EmmMappings[physicalPage].Page = EmmNullPage;
            return EmsErrors.EmmNoError;
        }
        /* Check for valid handle */
        if (!ValidateHandle(handle)) {
            return EmsErrors.EmmInvalidHandle;
        }

        if (logicalPage < _machine.EmsCard.EmmHandles[handle].Pages) {
            /* Mapping it is */
            _machine.EmsCard.EmmMappings[physicalPage].Handle = handle;
            _machine.EmsCard.EmmMappings[physicalPage].Page = logicalPage;
            return EmsErrors.EmmNoError;
        } else {
            /* Illegal logical page it is */
            return EmsErrors.EmsLogicalPageOutOfRange;
        }
    }
    
    public bool ValidateHandle(ushort handle) {
        if (handle >= EmmMaxHandles) {
            return false;
        }
        return _machine.EmsCard.EmmHandles[handle].Pages != EmmNullHandle;
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
        _state.AH = 0;
    }
    
    /// <summary>
    /// Saves the current state of page map registers for a handle.
    /// </summary>
    public void SavePageMap() {
    }
    
    /// <summary>
    /// Restores the state of page map registers for a handle.
    /// </summary>
    public void RestorePageMap() {
    }
    
    public void GetHandleCount() {
        // Return the number of EMM handles (plus 1 for the OS handle).
        _state.BX = (ushort)(_machine.EmsCard.EmmHandles.Length + 1);
        // Return good status.
        _state.AH = EmsErrors.EmmNoError;
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
            _state.BX = _machine.EmsCard.FreePages;
            // Return total number of pages in DX.
            _state.DX = (ushort) _machine.EmsCard.TotalPages;
            // Set good status.
            _state.AH = EmsErrors.EmmNoError;
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
            return EmsErrors.EmmZeroPages;
        }
        
        // Check for enough free pages
        if (_machine.EmsCard.FreeMemoryTotal / 4 < pages) {
            return EmsErrors.EmmOutOfLogicalPages;
        }

        ushort handle = 1;
        // Check for a free handle
        while (_machine.EmsCard.EmmHandles[handle].Pages > 0) {
            if (++handle >= EmmMaxHandles) {
                return EmsErrors.EmmOutOfHandles;
            }
        }

        int mem;
        if (pages != 0) {
            mem = _machine.EmsCard.AllocatePages((ushort)(pages * 4), false);
            if (mem == 0) {
                throw new UnrecoverableException("EMS: Memory allocation failure");
            }

            _machine.EmsCard.EmmHandles[handle].Pages = pages;
            _machine.EmsCard.EmmHandles[handle].MemHandle = mem;
            // Change handle only if there is no error.
            dhandle = handle;
        }
        return EmsErrors.EmmNoError;
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
