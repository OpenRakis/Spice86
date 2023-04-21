﻿namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Serilog;
using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

/// <summary>
/// Provides DOS applications with EMS memory. <br/>
/// Expanded memory is memory beyond DOS's 640K-byte limit.  The LIM <br/>
/// specification supports up to 32M bytes of expanded memory.  Because <br/>
/// the 8086, 8088, and 80286 (in real mode) microprocessors can <br/>
/// physically address only 1M byte of memory, they access expanded memory <br/>
/// through a window in their physical address range. 
/// </summary>
public sealed class ExpandedMemoryManager : InterruptHandler {
    /// <summary>
    /// The string identifier in main memory for the EMS Handler. <br/>
    /// DOS programs can detect the presence of an EMS handler by looking for it <br/>
    /// (this is one, of two, methods to do so).
    /// </summary>
    public const string EmsIdentifier = "EMMXXXX0";
    
    /// <summary>
    /// The maximum number of handles we can have allocated at the same time. <br/>
    /// A handle corresponds to one or more logical pages.
    /// </summary>
    public const byte EmmMaxHandles = 16;

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
    
    public override byte Index => 0x67;

    private readonly ILogger _loggerService;

    /// <summary>
    /// Tne entire Emm Memory, divided into logical pages.
    /// </summary>
    public EmmMemory EmmMemory { get; } = new();
    
    /// <summary>
    /// Because the 8086, 8088, and 80286 (in real mode) microprocessors can
    /// physically address only 1M byte of memory, they access expanded memory
    /// through a window in their physical address range. <br/>
    /// This is referred as the Emm Page Frame.
    /// </summary>
    public EmmPage[] EmmPageFrame { get; init; } = new EmmPage[EmmMaxPhysicalPages];

    /// <summary>
    /// The links between EMM handles given to the DOS program, and on or more logical pages.
    /// </summary>
    public EmmHandle[] EmmHandles { get; } = new EmmHandle[EmmMaxHandles];

    public ExpandedMemoryManager(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        _loggerService = loggerService.WithLogLevel(LogEventLevel.Debug);
        var device = new CharacterDevice(DeviceAttributes.Ioctl, EmsIdentifier);
        machine.Dos.AddDevice(device);
        FillDispatchTable();

        for (ushort i = 0; i < EmmMaxPhysicalPages; i++) {
            EmmPageFrame[i] = new() {
                PageNumber = i
            };
            _memory.RegisterMapping(MemoryUtils.ToPhysicalAddress(EmmPageFrameSegment, (ushort) (EmmPageSize * i)), EmmPageSize, EmmPageFrame[i].PageMemory);
        }
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x40, new Callback(0x40, GetStatus));
        _dispatchTable.Add(0x41, new Callback(0x41, GetPageFrameSegment));
        _dispatchTable.Add(0x42, new Callback(0x42, GetNumberOfPages));
        //_dispatchTable.Add(0x43, new Callback(0x43, GetHandleAndAllocatePages));
        //_dispatchTable.Add(0x44, new Callback(0x44, MapExpandedMemoryPage));
        //_dispatchTable.Add(0x45, new Callback(0x45, ReleaseHandleAndFreePages));
        _dispatchTable.Add(0x46, new Callback(0x46, GetEmmVersion));
        //_dispatchTable.Add(0x47, new Callback(0x47, SavePageMap));
        //_dispatchTable.Add(0x48, new Callback(0x48, RestorePageMap));
        //_dispatchTable.Add(0x4B, new Callback(0x4B, GetHandleCount));
        //_dispatchTable.Add(0x4C, new Callback(0x4C, GetPagesForOneHandle));
        //_dispatchTable.Add(0x4D, new Callback(0x4D, GetPageForAllHandles));
        //_dispatchTable.Add(0x4E, new Callback(0x4E, SaveOrRestorePageMap));
        //_dispatchTable.Add(0x4F, new Callback(0x4F, SaveOrRestorePartialPageMap));
        //_dispatchTable.Add(0x50, new Callback(0x50, MapOrUnmapMultipleHandlePages));
        //_dispatchTable.Add(0x51, new Callback(0x51, ReallocatePages));
        //_dispatchTable.Add(0x53, new Callback(0x53, SetGetHandleName));
        //_dispatchTable.Add(0x54, new Callback(0x54, HandleFunctions));
        //_dispatchTable.Add(0x58, new Callback(0x58, GetMappablePhysicalArrayAddressArray));
        //_dispatchTable.Add(0x59, new Callback(0x59, GetHardwareInformation));
        //_dispatchTable.Add(0x5A, new Callback(0x5A, AllocateStandardRawPages));
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
    /// The Get Unallocated Page Count function returns the number of
    /// unallocated pages (pages available to your program),
    /// and the total number of pages in expanded memory.
    /// </summary>
    public void GetNumberOfPages() {
        // Return total number of pages in DX.
        _state.DX = (ushort) (EmmMemory.TotalPages / 4);
        // Return number of pages available in BX.
        _state.BX = EmmMemory.GetFreePages();
        // Set good status.
        _state.AH = EmmStatus.EmmNoError;
        if (_loggerService.IsEnabled(LogEventLevel.Debug))
            _loggerService.Debug("EMS: {@MethodName}: Total: {@Total} Free: {@Free}", nameof(GetNumberOfPages), _state.DX, _state.BX);
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

    public override void Run() {
        byte operation = _state.AH;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose))
            _loggerService.Verbose("EMS function: 0x{@Function:X2} AL=0x{Al:X2}", operation, _state.AL);
        if (!_dispatchTable.ContainsKey(operation)) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("EMS function not provided: {@Function}", operation);
            }
            _state.AH = EmmStatus.EmmFuncNoSup;
        }
        Run(operation);
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