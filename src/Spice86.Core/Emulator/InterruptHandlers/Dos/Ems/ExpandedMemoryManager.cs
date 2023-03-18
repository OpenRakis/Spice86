using Serilog;
using Serilog.Events;

using Spice86.Core.Emulator.Devices.Memory;
using Spice86.Logging;

using System.Numerics;

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
    public const ushort PageFrameSegment = 0xE000;

    public const string EmsIdentifier = "EMMXXXX0";

    public const ushort EmmMaxHandles = 200;
    

    public Memory ExpandedMemory { get; init; }

    public override ushort? InterruptHandlerSegment => 0xF100;

    private readonly ILoggerService _loggerService;

    public ExpandedMemoryManager(Machine machine, ILoggerService loggerService) : base(machine) {
        _loggerService = loggerService;
        ExpandedMemory = new(8 * 1024);
        var device = new CharacterDevice(DeviceAttributes.Ioctl, EmsIdentifier);
        machine.Dos.AddDevice(device, InterruptHandlerSegment, 0x0000);

        FillDispatchTable();
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
        _dispatchTable.Add(0x50, new Callback(0x50, MapOrUnmapMultiplePageMap));
        _dispatchTable.Add(0x51, new Callback(0x51, ReallocatePages));
        _dispatchTable.Add(0x53, new Callback(0x53, SetGetHandleName));
        _dispatchTable.Add(0x57, new Callback(0x57, MemoryRegion));
        _dispatchTable.Add(0x59, new Callback(0x59, GetHardwareInformation));
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
        // Return total number of pages in DX.
        _state.DX = (ushort) (_machine.EmsCard.TotalPages / 4);
        // Return number of pages available in BX.
        _state.BX = _machine.EmsCard.FreePages;
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

    public void GetHandleCount() {
        // Return the number of EMM handles (plus 1 for the OS handle).
        _state.BX = (ushort)(_machine.EmsCard.EmmHandles.Length + 1);
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
                //Move();
                break;

            default:
                throw new NotImplementedException($"EMM function 57{_state.AL:X2}h not implemented.");
        }
    }


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
        ushort handles = _state.DX;
        _state.AX = EmmAllocateMemory(_state.BX, ref handles, false);
        _state.DX = handles;
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
            dhandle = handle;
        }
        return EmsErrors.EmmNoError;
    }

    /// <summary>
    /// Reallocates pages for a handle.
    /// </summary>
    public void ReallocatePages() {
        
    }

    /// <summary>
    /// Deallocates a handle and all of its pages.
    /// </summary>
    public void ReleaseHandleAndFreePages() {
        int handle = _state.DX;
    }

    /// <summary>
    /// Maps or unmaps a physical page.
    /// </summary>
    public void MapExpandedMeoryPage() {
        int physicalPage = _state.AL;
    }

    /// <summary>
    /// Gets the number of pages allocated to a handle.
    /// </summary>
    public void GetPagesForOneHandle() {
        int handleIndex = _state.DX;
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
}
