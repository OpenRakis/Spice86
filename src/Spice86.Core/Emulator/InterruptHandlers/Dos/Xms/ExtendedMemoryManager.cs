namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

using Spice86.Core;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

/// <summary>
/// Provides DOS applications with XMS memory according to the eXtended Memory Specification (XMS) version 3.0. <br/>
/// XMS allows DOS programs to utilize additional memory found in Intel's 80286 and 80386 based machines in
/// a consistent, machine independent manner. XMS adds almost 64K to the 640K which DOS programs can access
/// directly and provides a standard method of storing data in extended memory above 1MB.
/// </summary>
/// <remarks>
/// <code>
/// Memory Layout:
/// |-------------------------------------------------------|   Top of Memory
/// |             Extended Memory Blocks (EMBs)             |
/// |            Used for data storage only                 |
/// |-------------------------------------------------------|   1088K
/// |           High Memory Area (HMA) - 64K-16B           |
/// |        FFFF:0010 through FFFF:FFFF                   |
/// |=======================================================|   1024K or 1MB
/// |         Upper Memory Blocks (UMBs) - Optional        |
/// |-------------------------------------------------------|   640K
/// |            Conventional DOS Memory                    |
/// +-------------------------------------------------------+   0K
/// </code>
/// This implementation provides XMS version 3.0 features accessed via INT 2Fh, AH=43h.
/// See: <c>xms20.txt</c> for the full specification.
/// In MS-DOS, this is HIMEM.SYS. In DOSBox, this is xms.cpp <br/>
/// In MS-DOS, EMM386.EXE uses XMS for EMS storage. This is not the case here.
/// </remarks>
public sealed class ExtendedMemoryManager : IVirtualDevice, IMemoryDevice {
    public const ushort XmsVersion = 0x0300;
    public const ushort XmsInternalVersion = 0x0301;

    private int _a20EnableCount;
    private bool _a20GlobalEnabled = false;
    private uint _a20LocalEnableCount = 0;
    private const uint A20MaxTimesEnabled = uint.MaxValue;

    private readonly ILoggerService _loggerService;
    private readonly State _state;
    private readonly A20Gate _a20Gate;
    private readonly IMemory _memory;
    private readonly LinkedList<XmsBlock> _xmsBlocksLinkedList = new();
    private readonly SortedList<int, int> _xmsHandles = new();

    /// <summary>
    /// The segment of the XMS Dos Device Driver.
    /// </summary>
    public const ushort DosDeviceSegment = MemoryMap.DeviceDriversSegment;

    /// <summary>
    /// The size of available XMS Memory, in kilobytes.
    /// </summary>
    /// <remarks>32 MB maximum size in the XMS 2.0 specification, but 8 MB available here.</remarks>
    public const uint XmsMemorySize = 8 * 1024;

    /// <summary>
    /// Specifies the starting physical address of XMS memory. <br/>
    /// XMS blocks start at 1088K, after the High Memory Area (HMA).
    /// </summary>
    public const uint XmsBaseAddress = 0x10FFF0;

    /// <summary>
    /// Maximum number of XMS handles that can be allocated simultaneously. <br/>
    /// This is the default value from HIMEM.SYS and can be adjusted via the /NUMHANDLES= parameter.
    /// </summary>
    private const int MaxHandles = 128;

    /// <summary>
    /// XMS plain old memory.
    /// </summary>
    public Ram XmsRam { get; private set; } = new(XmsMemorySize * 1024);

    /// <summary>
    /// DOS Device Driver Name.
    /// </summary>
    public const string XmsIdentifier = "XMSXXXX0";

    /// <summary>
    /// The memory address to the C# XMS callback <see cref="RunMultiplex"/>
    /// </summary>
    public SegmentedAddress CallbackAddress { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedMemoryManager"/> class.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="a20Gate">The A20 gate controller.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public ExtendedMemoryManager(IMemory memory, State state, A20Gate a20Gate,
        MemoryAsmWriter memoryAsmWriter, DosTables dosTables,
        ILoggerService loggerService) {
        uint headerAddress = new SegmentedAddress(DosDeviceSegment, 0x0).Linear;
        Header = new DosDeviceHeader(memory,
            headerAddress) {
            Name = XmsIdentifier,
            StrategyEntryPoint = 0,
            InterruptEntryPoint = 0
        };
        _state = state;
        _a20Gate = a20Gate;
        _a20Gate.IsEnabled = true;
        _memory = memory;
        _loggerService = loggerService;
        // Place hookable callback in writable memory area
        var hookableCodeAddress = new SegmentedAddress((ushort)(dosTables.GetDosPrivateTableWritableAddress(0x1) - 1), 0x10);
        CallbackAddress = hookableCodeAddress;
        SegmentedAddress savedAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.CurrentAddress = hookableCodeAddress;
        memoryAsmWriter.WriteJumpNear(0x3);
        memoryAsmWriter.WriteNop();
        memoryAsmWriter.WriteNop();
        memoryAsmWriter.WriteNop();
        memoryAsmWriter.RegisterAndWriteCallback(0x43, RunMultiplex);
        memoryAsmWriter.WriteIret();
        memoryAsmWriter.WriteFarRet();
        memoryAsmWriter.CurrentAddress = savedAddress;
        memory.RegisterMapping(XmsBaseAddress, XmsMemorySize * 1024, this);
        Name = XmsIdentifier;
    }

    /// <summary>
    /// Gets the largest free block of memory in bytes.
    /// </summary>
    public uint LargestFreeBlock => GetFreeBlocks().FirstOrDefault().Length;

    /// <summary>
    /// Gets the total amount of free memory in bytes.
    /// </summary>
    public long TotalFreeMemory => GetFreeBlocks().Sum(b => b.Length);

    /// <summary>
    /// Dispatches XMS subfunctions based on the value in AH.
    /// </summary>
    /// <remarks>
    /// This is the main entry point for XMS API calls via the multiplex interrupt.
    /// </remarks>
    public void RunMultiplex() {
        var operation = (XmsSubFunctionsCodes)_state.AH;
        switch (operation) {
            case XmsSubFunctionsCodes.GetVersionNumber:
                GetVersionNumber();
                break;
            case XmsSubFunctionsCodes.RequestHighMemoryArea:
                RequestHighMemoryArea();
                break;
            case XmsSubFunctionsCodes.ReleaseHighMemoryArea:
                ReleaseHighMemoryArea();
                break;
            case XmsSubFunctionsCodes.GlobalEnableA20:
                GlobalEnableA20();
                break;
            case XmsSubFunctionsCodes.GlobalDisableA20:
                GlobalDisableA20();
                break;
            case XmsSubFunctionsCodes.LocalEnableA20:
                EnableLocalA20();
                break;
            case XmsSubFunctionsCodes.LocalDisableA20:
                DisableLocalA20();
                break;
            case XmsSubFunctionsCodes.QueryA20:
                QueryA20();
                break;
            case XmsSubFunctionsCodes.QueryFreeExtendedMemory:
                QueryFreeExtendedMemory();
                break;
            case XmsSubFunctionsCodes.AllocateExtendedMemoryBlock:
                AllocateExtendedMemoryBlock();
                break;
            case XmsSubFunctionsCodes.FreeExtendedMemoryBlock:
                FreeExtendedMemoryBlock();
                break;
            case XmsSubFunctionsCodes.MoveExtendedMemoryBlock:
                MoveExtendedMemoryBlock();
                break;
            case XmsSubFunctionsCodes.LockExtendedMemoryBlock:
                LockExtendedMemoryBlock();
                break;
            case XmsSubFunctionsCodes.UnlockExtendedMemoryBlock:
                UnlockExtendedMemoryBlock();
                break;
            case XmsSubFunctionsCodes.GetHandleInformation:
                GetHandleInformation();
                break;
            case XmsSubFunctionsCodes.ReallocateExtendedMemoryBlock:
                ReallocateExtendedMemoryBlock();
                break;
            case XmsSubFunctionsCodes.RequestUpperMemoryBlock:
                RequestUpperMemoryBlock();
                break;
            case XmsSubFunctionsCodes.ReleaseUpperMemoryBlock:
                ReleaseUpperMemoryBlock();
                break;
            case XmsSubFunctionsCodes.ReallocateUpperMemoryBlock:
            case XmsSubFunctionsCodes.QueryAnyFreeExtendedMemory:
            case XmsSubFunctionsCodes.AllocateAnyExtendedMemory:
                // XMS 3.0 extended functions - not implemented yet
                if(_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                    _loggerService.Error("XMS 3.0 function not implemented: {XmsSubFunctionNumber:X2}", operation);
                }
                _state.AX = 0x0;
                _state.BH = 0xFF;
                _state.BL = (byte)XmsErrorCodes.NotImplemented;
                break;
            default:
                if(_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                    _loggerService.Error("XMS function not recognized: {XmsSubFunctionNumber:X2}", operation);
                }
                _state.AX = 0x0;
                _state.BH = 0xFF;
                _state.BL = (byte)XmsErrorCodes.NotImplemented;
                break;
        }
    }

    /// <summary>
    /// XMS Function 00h: Get XMS Version Number.
    /// Returns the XMS version, driver revision, and HMA existence.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 00h<br/>
    /// <b>Outputs:</b>
    /// <list type="bullet">
    /// <item>AX = XMS version number (BCD, e.g. 0200h for 2.00)</item>
    /// <item>BX = Driver internal revision number</item>
    /// <item>DX = 0001h if HMA exists, 0000h otherwise</item>
    /// </list>
    /// <b>Errors:</b> None
    /// </remarks>
    public void GetVersionNumber() {
        _state.AX = XmsVersion; // XMS version 3.00
        _state.BX = XmsInternalVersion;
        _state.DX = 0x0;      // HMA is not available.
    }

    /// <summary>
    /// XMS Function 01h: Request High Memory Area (HMA).
    /// Attempts to reserve the 64K-16 byte HMA for the caller.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 01h, DX = FFFFh (application) or size in bytes (TSR/driver)<br/>
    /// <b>Outputs:</b> AX = 0001h if assigned, 0000h otherwise<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = 90h (HMA does not exist)</item>
    /// <item>BL = 91h (HMA already in use)</item>
    /// <item>BL = 92h (DX &lt; /HMAMIN= parameter)</item>
    /// </list>
    /// </remarks>
    public void RequestHighMemoryArea() {
        // Not implemented - fail with "already in use"
        _state.AX = 0;
        _state.BL = 0x91;
    }

    /// <summary>
    /// XMS Function 02h: Release High Memory Area (HMA).
    /// Releases the HMA, making it available for other programs.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 02h<br/>
    /// <b>Outputs:</b> AX = 0001h if released, 0000h otherwise<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = 90h (HMA does not exist)</item>
    /// <item>BL = 93h (HMA not allocated)</item>
    /// </list>
    /// </remarks>
    public void ReleaseHighMemoryArea() {
        // Not implemented - fail with "not allocated"
        _state.AX = 0;
        _state.BL = 0x93;
    }

    /// <summary>
    /// XMS Function 03h: Global Enable A20.
    /// Attempts to enable the A20 line globally.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 03h<br/>
    /// <b>Outputs:</b> AX = 0001h if enabled, 0000h otherwise<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = 82h (A20 error)</item>
    /// </list>
    /// </remarks>
    public void GlobalEnableA20() {
        _a20GlobalEnabled = true;
        SetA20(true);
        _state.AX = 1;
    }

    /// <summary>
    /// XMS Function 04h: Global Disable A20.
    /// Attempts to disable the A20 line globally.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 04h<br/>
    /// <b>Outputs:</b> AX = 0001h if disabled, 0000h otherwise<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = 82h (A20 error)</item>
    /// <item>BL = 94h (A20 still enabled)</item>
    /// </list>
    /// </remarks>
    public void GlobalDisableA20() {
        _a20GlobalEnabled = false;
        SetA20(false);
        _state.AX = 1;
    }

    /// <summary>
    /// XMS Function 05h: Local Enable A20.
    /// Increments the local A20 enable count and enables A20 if needed.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 05h<br/>
    /// <b>Outputs:</b> AX = 0001h if enabled, 0000h otherwise<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = 82h (A20 error)</item>
    /// </list>
    /// </remarks>
    public void EnableLocalA20() {
        // Counter overflow protection
        if (_a20LocalEnableCount == A20MaxTimesEnabled) {
            _state.AX = 0;
            _state.BL = (byte)XmsErrorCodes.A20Error;
            return;
        }

        // Only enable A20 if count is 0
        if (_a20LocalEnableCount++ == 0) {
            SetA20(true);
        }

        _state.AX = 1;
    }

    /// <summary>
    /// XMS Function 06h: Local Disable A20.
    /// Decrements the local A20 enable count and disables A20 if needed.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 06h<br/>
    /// <b>Outputs:</b> AX = 0001h if successful, 0000h otherwise<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = 82h (A20 error)</item>
    /// <item>BL = 94h (A20 still enabled)</item>
    /// </list>
    /// </remarks>
    public void DisableLocalA20() {
        // HIMEM.SYS returns error if count is already 0
        if (_a20LocalEnableCount == 0) {
            _state.AX = 0;
            _state.BL = (byte)XmsErrorCodes.A20Error;
            return;
        }

        if (--_a20LocalEnableCount != 0) {
            // A20 line is still enabled, per spec
            _state.AX = 0;
            _state.BL = (byte)XmsErrorCodes.A20StillEnabled;
            return;
        }

        SetA20(false);
        _state.AX = 1;
    }

    /// <summary>
    /// XMS Function 07h: Query A20.
    /// Checks if the A20 line is physically enabled.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 07h<br/>
    /// <b>Outputs:</b> AX = 0001h if enabled, 0000h otherwise<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 00h (success)</item>
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// </list>
    /// </remarks>
    public void QueryA20() {
        _state.AX = IsA20Enabled() ? (ushort)1 : (ushort)0;
    }

    /// <summary>
    /// XMS Function 08h: Query Free Extended Memory.
    /// Returns the size of the largest free block and total free memory in K-bytes.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 08h<br/>
    /// <b>Outputs:</b> AX = Largest free block in K-bytes, DX = Total free memory in K-bytes<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = A0h (all memory allocated)</item>
    /// </list>
    /// </remarks>
    public void QueryFreeExtendedMemory() {
        if (LargestFreeBlock <= ushort.MaxValue * 1024u) {
            _state.AX = (ushort)(LargestFreeBlock / 1024u);
        } else {
            _state.AX = ushort.MaxValue;
        }

        if (TotalFreeMemory <= ushort.MaxValue * 1024u) {
            _state.DX = (ushort)(TotalFreeMemory / 1024);
        } else {
            _state.DX = ushort.MaxValue;
        }

        if (_state.AX == 0 && _state.DX == 0) {
            _state.BL = 0xA0;
        }
    }

    /// <summary>
    /// XMS Function 09h: Allocate Extended Memory Block.
    /// Allocates a block of extended memory of the requested size.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 09h, DX = Size in K-bytes<br/>
    /// <b>Outputs:</b> AX = 0001h if allocated, 0000h otherwise; DX = Handle to allocated block<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = A0h (no memory)</item>
    /// <item>BL = A1h (no handles)</item>
    /// </list>
    /// </remarks>
    public void AllocateExtendedMemoryBlock() {
        AllocateAnyExtendedMemory(_state.DX);
    }

    /// <summary>
    /// XMS Function 0Ah: Free Extended Memory Block.
    /// Frees a previously allocated extended memory block.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 0Ah, DX = Handle to free<br/>
    /// <b>Outputs:</b> AX = 0001h if freed, 0000h otherwise<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = A2h (invalid handle)</item>
    /// <item>BL = ABh (handle locked)</item>
    /// </list>
    /// </remarks>
    public void FreeExtendedMemoryBlock() {
        int handle = _state.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            _state.AX = 0;
            _state.BL = 0xA2;
            return;
        }

        if (lockCount > 0) {
            _state.AX = 0;
            _state.BL = 0xAB;
            return;
        }

        if (TryGetBlock(handle, out XmsBlock block)) {
            XmsBlock freeBlock = block.Free();
            _xmsBlocksLinkedList.Replace(block, freeBlock);
            MergeFreeBlocks(freeBlock);
        }

        _xmsHandles.Remove(handle);
        _state.AX = 1;
    }

    /// <summary>
    /// XMS Function 0Bh: Move Extended Memory Block.
    /// Moves a block of memory as described by the Extended Memory Move Structure at DS:SI.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 0Bh, DS:SI = Pointer to ExtendedMemoryMoveStructure<br/>
    /// <b>Outputs:</b> AX = 0001h if successful, 0000h otherwise<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = 82h (A20 error)</item>
    /// <item>BL = A3h (invalid source handle)</item>
    /// <item>BL = A4h (invalid source offset)</item>
    /// <item>BL = A5h (invalid dest handle)</item>
    /// <item>BL = A6h (invalid dest offset)</item>
    /// <item>BL = A7h (invalid length)</item>
    /// <item>BL = A8h (invalid overlap)</item>
    /// <item>BL = A9h (parity error)</item>
    /// </list>
    /// </remarks>
    public unsafe void MoveExtendedMemoryBlock() {
        bool a20State = IsA20Enabled();
        SetA20(true);

        uint address = MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI);
        ExtendedMemoryMoveStructure moveData = new(_memory, address);
        Span<byte> srcPtr = new byte[] { };
        Span<byte> destPtr = new byte[] { };

        if (moveData.SourceHandle == 0) {
            SegmentedAddress srcAddress = moveData.SourceAddress;
            srcPtr = _memory.GetSpan(srcAddress.Segment, srcAddress.Offset);
        } else {
            if (TryGetBlock(moveData.SourceHandle, out XmsBlock srcBlock)) {
                srcPtr = _memory.GetSpan((int)(XmsBaseAddress + srcBlock.Offset + moveData.SourceOffset), 0);
            }
        }

        if (moveData.DestHandle == 0) {
            SegmentedAddress destAddress = moveData.DestAddress;
            destPtr = _memory.GetSpan(destAddress.Segment, destAddress.Offset);
        } else {
            if (TryGetBlock(moveData.DestHandle, out XmsBlock destBlock)) {
                destPtr = _memory.GetSpan((int)(XmsBaseAddress + destBlock.Offset + moveData.DestOffset), 0);
            }
        }

        if (srcPtr.Length == 0) {
            _state.BL = 0xA3;
            _state.AX = 0;
            return;
        }

        if (destPtr.Length == 0) {
            _state.BL = 0xA5;
            _state.AX = 0;
            return;
        }

        srcPtr.CopyTo(destPtr);

        _state.AX = 1;
        SetA20(a20State);
    }

    /// <summary>
    /// XMS Function 0Ch: Lock Extended Memory Block.
    /// Locks a block and returns its 32-bit linear address.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 0Ch, DX = Handle to lock<br/>
    /// <b>Outputs:</b> AX = 0001h if locked, 0000h otherwise; DX:BX = 32-bit linear address<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = A2h (invalid handle)</item>
    /// <item>BL = ACh (lock count overflow)</item>
    /// <item>BL = ADh (lock fails)</item>
    /// </list>
    /// </remarks>
    public void LockExtendedMemoryBlock() {
        int handle = _state.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            _state.AX = 0;
            _state.BL = 0xA2;
            return;
        }

        _xmsHandles[handle] = lockCount + 1;

        _ = TryGetBlock(handle, out XmsBlock block);
        uint fullAddress = XmsBaseAddress + block.Offset;

        _state.AX = 1;
        _state.DX = (ushort)(fullAddress >> 16);
        _state.BX = (ushort)(fullAddress & 0xFFFFu);
    }

    /// <summary>
    /// XMS Function 0Dh: Unlock Extended Memory Block.
    /// Unlocks a previously locked block.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 0Dh, DX = Handle to unlock<br/>
    /// <b>Outputs:</b> AX = 0001h if unlocked, 0000h otherwise<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = A2h (invalid handle)</item>
    /// <item>BL = AAh (not locked)</item>
    /// </list>
    /// </remarks>
    public void UnlockExtendedMemoryBlock() {
        int handle = _state.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            _state.AX = 0;
            _state.BL = 0xA2;
            return;
        }

        if (lockCount < 1) {
            _state.AX = 0;
            _state.BL = 0xAA;
            return;
        }

        _xmsHandles[handle] = lockCount - 1;

        _state.AX = 1;
    }

    /// <summary>
    /// XMS Function 0Eh: Get Handle Information.
    /// Returns lock count, free handles, and block size for a handle.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 0Eh, DX = Handle<br/>
    /// <b>Outputs:</b> AX = 0001h if found, 0000h otherwise; BH = Lock count; BL = Free handles; DX = Block length in K-bytes<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = A2h (invalid handle)</item>
    /// </list>
    /// </remarks>
    public void GetHandleInformation() {
        int handle = _state.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            _state.AX = 0;
            _state.BL = 0xA2;
            return;
        }

        _state.BH = (byte)lockCount;
        _state.BL = (byte)(MaxHandles - _xmsHandles.Count);

        if (!TryGetBlock(handle, out XmsBlock block)) {
            _state.DX = 0;
        } else {
            _state.DX = (ushort)(block.Length / 1024u);
        }

        _state.AX = 1;
    }

    /// <summary>
    /// XMS Function 0Fh: Reallocate Extended Memory Block.
    /// Changes the size of an unlocked extended memory block.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 0Fh, BX = New size in K-bytes, DX = Handle<br/>
    /// <b>Outputs:</b> AX = 0001h if reallocated, 0000h otherwise<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = 81h (VDISK detected)</item>
    /// <item>BL = A0h (no memory)</item>
    /// <item>BL = A1h (no handles)</item>
    /// <item>BL = A2h (invalid handle)</item>
    /// <item>BL = ABh (block locked)</item>
    /// </list>
    /// </remarks>
    private void ReallocateExtendedMemoryBlock() {
        // Not implemented
        _state.AX = 0;
        _state.BL = 0x80;
    }

    /// <summary>
    /// XMS Function 10h: Request Upper Memory Block (UMB).
    /// Attempts to allocate a UMB of the requested size.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 10h, DX = Size in paragraphs<br/>
    /// <b>Outputs:</b> AX = 0001h if granted, 0000h otherwise; BX = Segment of UMB; DX = Actual size or largest available<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = B0h (smaller UMB)</item>
    /// <item>BL = B1h (no UMBs)</item>
    /// </list>
    /// </remarks>
    public void RequestUpperMemoryBlock() {
        _state.BL = 0xB1; // No UMBs available
        _state.AX = 0;
    }

    /// <summary>
    /// XMS Function 11h: Release Upper Memory Block (UMB).
    /// Releases a previously allocated UMB.
    /// </summary>
    /// <remarks>
    /// <b>Inputs:</b> AH = 11h, DX = Segment of UMB<br/>
    /// <b>Outputs:</b> AX = 0001h if released, 0000h otherwise<br/>
    /// <b>Errors:</b>
    /// <list type="bullet">
    /// <item>BL = 80h (not implemented)</item>
    /// <item>BL = B2h (invalid segment)</item>
    /// </list>
    /// </remarks>
    private void ReleaseUpperMemoryBlock() {
        _state.AX = 0;
        _state.BL = 0x80;
    }

    /// <summary>
    /// Attempts to allocate a block of extended memory.
    /// </summary>
    /// <param name="length">Number of bytes to allocate.</param>
    /// <param name="handle">If successful, contains the allocation handle.</param>
    /// <returns>Zero on success. Nonzero indicates error code.</returns>
    public byte TryAllocate(uint length, out short handle) {
        handle = (short)GetNextHandle();
        if (handle == 0) {
            return 0xA1; // All handles are used.
        }

        // Round up to next kbyte if necessary.
        if (length % 1024 != 0) {
            length = (length & 0xFFFFFC00u) + 1024u;
        } else {
            length &= 0xFFFFFC00u;
        }

        // Zero-length allocations are allowed.
        if (length == 0) {
            _xmsHandles.Add(handle, 0);
            return 0;
        }

        XmsBlock? smallestFreeBlock = GetFreeBlocks()
            .Where(b => b.Length >= length)
            .Select(static b => new XmsBlock?(b))
            .FirstOrDefault();

        if (smallestFreeBlock == null) {
            return 0xA0; // Not enough free memory.
        }

        LinkedListNode<XmsBlock>? freeNode = _xmsBlocksLinkedList.Find(smallestFreeBlock.Value);
        if (freeNode is not null) {
            XmsBlock[] newNodes = freeNode.Value.Allocate(handle, length);
            _xmsBlocksLinkedList.Replace((XmsBlock)smallestFreeBlock, newNodes);
        }

        _xmsHandles.Add(handle, 0);
        return 0;
    }

    /// <summary>
    /// Returns the block with the specified handle if found; otherwise returns null.
    /// </summary>
    /// <param name="handle">Handle of block to search for.</param>
    /// <param name="block">On success, contains information about the block.</param>
    /// <returns>True if handle was found; otherwise false.</returns>
    public bool TryGetBlock(int handle, out XmsBlock block) {
        foreach (XmsBlock b in _xmsBlocksLinkedList.Where(b => !b.IsFree && b.Handle == handle)) {
            block = b;
            return true;
        }

        block = default;
        return false;
    }

    /// <summary>
    /// Returns all of the free blocks in the map sorted by size in ascending order.
    /// </summary>
    /// <returns>Sorted list of free blocks in the map.</returns>
    public IEnumerable<XmsBlock> GetFreeBlocks() => _xmsBlocksLinkedList.Where(static x => x.IsFree).OrderBy(static x => x.Length);

    /// <summary>
    /// Returns the next available handle for an allocation on success; returns 0 if no handles are available.
    /// </summary>
    /// <returns>New handle if available; otherwise returns null.</returns>
    public int GetNextHandle() {
        for (int i = 1; i <= MaxHandles; i++) {
            if (!_xmsHandles.ContainsKey(i)) {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Attempts to merge a free block with the following block if possible.
    /// </summary>
    /// <param name="firstBlock">Free block to merge.</param>
    public void MergeFreeBlocks(XmsBlock firstBlock) {
        LinkedListNode<XmsBlock>? firstNode = _xmsBlocksLinkedList.Find(firstBlock);

        if (firstNode?.Next != null) {
            LinkedListNode<XmsBlock> nextNode = firstNode.Next;
            if (nextNode.Value.IsFree) {
                XmsBlock newBlock = firstBlock.Join(nextNode.Value);
                _xmsBlocksLinkedList.Remove(nextNode);
                _xmsBlocksLinkedList.Replace(firstBlock, newBlock);
            }
        }
    }

    /// <summary>
    /// Allocates a new block of memory.
    /// </summary>
    /// <param name="kbytes">Number of kilobytes requested.</param>
    public void AllocateAnyExtendedMemory(uint kbytes) {
        byte res = TryAllocate(kbytes * 1024u, out short handle);
        if (res == 0) {
            _state.AX = 1; // Success.
            _state.DX = (ushort)handle;
        } else {
            _state.AX = 0; // Didn't work.
            _state.BL = res;
        }
    }

    private void SetA20(bool enable) {
        _a20Gate.IsEnabled = enable;
    }

    private bool IsA20Enabled() {
        return _a20Gate.IsEnabled;
    }

    /// <inheritdoc/>
    public uint Size => XmsMemorySize * 1024;

    /// <inheritdoc/>
    public uint DeviceNumber { get; set; }
   
    /// <inheritdoc/>
    public DosDeviceHeader Header { get; init; }

    /// <summary>
    /// Not supported by HIMEM.SYS
    /// </summary>
    public ushort Information => 0x0;
    
    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public byte Read(uint address) {
        return XmsRam.Read(address - XmsBaseAddress);
    }

    /// <inheritdoc/>
    public void Write(uint address, byte value) {
        XmsRam.Write(address - XmsBaseAddress, value);
    }

    /// <inheritdoc/>
    public Span<byte> GetSpan(int address, int length) {
        return XmsRam.GetSpan((int) (address - XmsBaseAddress), length);
    }

    /// <inheritdoc/>
    public byte GetStatus(bool inputFlag) {
        //Not supported by HIMEM.SYS
        return 0;
    }

    /// <inheritdoc/>
    public bool TryReadFromControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {
        //Not supported by HIMEM.SYS
        returnCode = null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryWriteToControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {
        //Not supported by HIMEM.SYS
        returnCode = null;
        return false;
    }

    /// <summary>
    /// BIOS-compatible function to copy extended memory, as used by INT 15h, AH=89h.
    /// This is not a standard XMS function, but is expected by BIOS and DOS programs.
    /// The copy parameters are passed in ES:SI as a structure compatible with <see cref="ExtendedMemoryMoveStructure"/>.
    /// </summary>
    /// <param name="calledFromVm">Indicates if called from the emulator.</param>
    /// <returns>True if the copy succeeded, false if there was an error.</returns>
    public bool CopyExtendedMemory(bool calledFromVm) {
        bool a20WasEnabled = IsA20Enabled();
        SetA20(true);

        uint moveStructAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.SI);
        var move = new ExtendedMemoryMoveStructure(_memory, moveStructAddress);

        // Validate length (must be even and nonzero)
        if (move.Length == 0 || (move.Length & 1) != 0) {
            _state.AH = 0x01; // Indicate error (arbitrary, as BIOS doesn't standardize this)
            SetA20(a20WasEnabled);
            return false;
        }

        // Determine source address
        uint srcAddress;
        if (move.SourceHandle == 0) {
            srcAddress = move.SourceAddress.Linear;
        } else if (TryGetBlock(move.SourceHandle, out XmsBlock srcBlock)) {
            srcAddress = XmsBaseAddress + srcBlock.Offset + move.SourceOffset;
        } else {
            _state.AH = 0x01; // Error: invalid source handle
            SetA20(a20WasEnabled);
            return false;
        }

        // Determine destination address
        uint destAddress;
        if (move.DestHandle == 0) {
            destAddress = move.DestAddress.Linear;
        } else if (TryGetBlock(move.DestHandle, out XmsBlock destBlock)) {
            destAddress = XmsBaseAddress + destBlock.Offset + move.DestOffset;
        } else {
            _state.AH = 0x01; // Error: invalid dest handle
            SetA20(a20WasEnabled);
            return false;
        }

        _memory.MemCopy(srcAddress, destAddress, move.Length);

        _state.AH = 0x00; // Success
        SetA20(a20WasEnabled);
        return true;
    }
}