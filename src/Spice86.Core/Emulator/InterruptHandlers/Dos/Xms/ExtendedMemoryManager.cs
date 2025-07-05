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
        _loggerService = loggerService.WithLogLevel(Serilog.Events.LogEventLevel.Verbose);
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
        memoryAsmWriter.WriteFarRet();
        memoryAsmWriter.CurrentAddress = savedAddress;
        //XMS driver takes ownership of the HMA
        memory.RegisterMapping(A20Gate.StartOfHighMemoryArea,
            A20Gate.EndOfHighMemoryArea - A20Gate.StartOfHighMemoryArea, this);
        //Add XMS memory
        memory.RegisterMapping(XmsBaseAddress, XmsMemorySize * 1024, this);
        Name = XmsIdentifier;

        // Initialize XMS memory as a single free block
        _xmsBlocksLinkedList.AddLast(new XmsBlock(0, 0, XmsMemorySize * 1024, true));
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
    /// <summary>
    /// Dispatches XMS subfunctions based on the value in AH.
    /// </summary>
    /// <remarks>
    /// This is the main entry point for XMS API calls via the multiplex interrupt.
    /// </remarks>
    public void RunMultiplex() {
        var operation = (XmsSubFunctionsCodes)_state.AH;

        _loggerService.Information("XMS call from CS:IP={CS:X4}:{IP:X4}, function {Function:X2}h",
            _state.CS, _state.IP, _state.AH);

        // Log detailed diagnostics for each XMS call
        string functionName = operation.ToString();
        string parameters = "";

        switch (operation) {
            case XmsSubFunctionsCodes.GetVersionNumber:
                parameters = "No parameters";
                break;
            case XmsSubFunctionsCodes.AllocateExtendedMemoryBlock:
                parameters = $"Size={_state.DX}KB";
                break;
            case XmsSubFunctionsCodes.FreeExtendedMemoryBlock:
                parameters = $"Handle={_state.DX:X4}h";
                break;
            case XmsSubFunctionsCodes.MoveExtendedMemoryBlock:
                uint structAddr = MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI);
                var move = new ExtendedMemoryMoveStructure(_memory, structAddr);
                parameters = $"Length={move.Length}, Src={move.SourceHandle:X4}h:{move.SourceOffset:X8}h, Dst={move.DestHandle:X4}h:{move.DestOffset:X8}h";
                break;
            case XmsSubFunctionsCodes.LockExtendedMemoryBlock:
            case XmsSubFunctionsCodes.UnlockExtendedMemoryBlock:
                parameters = $"Handle={_state.DX:X4}h";
                break;
            default:
                parameters = $"AX={_state.AX:X4}h BX={_state.BX:X4}h CX={_state.CX:X4}h DX={_state.DX:X4}h";
                break;
        }

        _loggerService.Information("XMS CALL: {Function} ({FuncCode:X2}h) with {Parameters} - CS:IP={CS:X4}:{IP:X4}",
            functionName, (byte)operation, parameters, _state.CS, _state.IP);

        // Execute the appropriate XMS function and get the result
        XmsResult result;

        switch (operation) {
            case XmsSubFunctionsCodes.GetVersionNumber:
                result = GetVersionNumber();
                break;

            case XmsSubFunctionsCodes.RequestHighMemoryArea:
                result = RequestHighMemoryArea();
                break;

            case XmsSubFunctionsCodes.ReleaseHighMemoryArea:
                result = ReleaseHighMemoryArea();
                break;

            case XmsSubFunctionsCodes.GlobalEnableA20:
                result = GlobalEnableA20();
                break;

            case XmsSubFunctionsCodes.GlobalDisableA20:
                result = GlobalDisableA20();
                break;

            case XmsSubFunctionsCodes.LocalEnableA20:
                result = EnableLocalA20();
                break;

            case XmsSubFunctionsCodes.LocalDisableA20:
                result = DisableLocalA20();
                break;

            case XmsSubFunctionsCodes.QueryA20:
                result = QueryA20();
                break;

            case XmsSubFunctionsCodes.QueryFreeExtendedMemory:
                result = QueryFreeExtendedMemory();
                break;

            case XmsSubFunctionsCodes.AllocateExtendedMemoryBlock:
                result = AllocateExtendedMemoryBlock();
                break;

            case XmsSubFunctionsCodes.FreeExtendedMemoryBlock:
                result = FreeExtendedMemoryBlock();
                break;

            case XmsSubFunctionsCodes.MoveExtendedMemoryBlock:
                result = MoveExtendedMemoryBlock();
                break;

            case XmsSubFunctionsCodes.LockExtendedMemoryBlock:
                result = LockExtendedMemoryBlock();
                break;

            case XmsSubFunctionsCodes.UnlockExtendedMemoryBlock:
                result = UnlockExtendedMemoryBlock();
                break;

            case XmsSubFunctionsCodes.GetHandleInformation:
                result = GetHandleInformation();
                break;

            case XmsSubFunctionsCodes.ReallocateExtendedMemoryBlock:
                result = ReallocateExtendedMemoryBlock();
                break;

            case XmsSubFunctionsCodes.RequestUpperMemoryBlock:
                result = RequestUpperMemoryBlock();
                break;

            case XmsSubFunctionsCodes.ReleaseUpperMemoryBlock:
                result = ReleaseUpperMemoryBlock();
                break;

            case XmsSubFunctionsCodes.QueryAnyFreeExtendedMemory:
                result = QueryAnyFreeExtendedMemory();
                break;

            case XmsSubFunctionsCodes.AllocateAnyExtendedMemory:
                result = AllocateAnyExtendedMemory();
                break;

            case XmsSubFunctionsCodes.GetExtendedEmbHandle:
                result = GetExtendedEmbHandle();
                break;

            case XmsSubFunctionsCodes.ReallocateAnyExtendedMemory:
                result = ReallocateAnyExtendedMemory();
                break;

            default:
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                    _loggerService.Error("XMS function not recognized: {XmsSubFunction:X2}h", (byte)operation);
                }
                result = XmsResult.Error(XmsErrorCodes.NotImplemented);
                break;
        }

        // Apply the results to the CPU registers according to XMS spec
        if (result.DirectAx) {
            // Special case for functions like version query that set AX directly
            _state.AX = result.PrimaryValue;
            _state.BX = result.SecondaryValue;
            _state.DX = result.TertiaryValue;
            _state.BL = 0;
        } else if (result.Success) {
            // Standard successful result
            _state.BL = 0;

            // Apply any additional return values based on function
            switch (operation) {
                case XmsSubFunctionsCodes.AllocateExtendedMemoryBlock:
                case XmsSubFunctionsCodes.AllocateAnyExtendedMemory:
                    _state.DX = result.PrimaryValue; // Handle
                    break;

                case XmsSubFunctionsCodes.LockExtendedMemoryBlock:
                    _state.DX = result.PrimaryValue; // High word of address
                    _state.BX = result.SecondaryValue; // Low word of address
                    break;

                case XmsSubFunctionsCodes.GetHandleInformation:
                    _state.BH = (byte)(result.PrimaryValue & 0xFF); // Lock count
                    _state.BL = (byte)(result.SecondaryValue & 0xFF); // Free handles (maintaining BL=0 for success)
                    _state.DX = result.TertiaryValue; // Block length
                    break;

                case XmsSubFunctionsCodes.QueryFreeExtendedMemory:
                    _state.AX = result.PrimaryValue;  // Largest block
                    _state.DX = result.SecondaryValue; // Total free memory
                    break;

                case XmsSubFunctionsCodes.QueryAnyFreeExtendedMemory:
                    _state.EAX = result.ExtendedResult;   // Largest block 
                    _state.EDX = result.PrimaryValue;     // Total free memory (32-bit)
                    _state.ECX = result.SecondaryValue;   // Highest memory address
                    break;

                case XmsSubFunctionsCodes.GetExtendedEmbHandle:
                    _state.BH = (byte)(result.PrimaryValue & 0xFF);  // Lock count
                    _state.CX = result.SecondaryValue;               // Free handle count
                    _state.EDX = result.ExtendedResult;              // Block length (32-bit)
                    break;

                    // For others, no additional registers need to be set
            }
        } else {
            // Failure result
            _state.BL = result.ErrorCode;
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("XMS Function {Function:X2}h returned AX={AX:X4}h BL={BL:X2}h",
                (byte)operation, _state.AX, _state.BL);
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
    public XmsResult GetVersionNumber() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS GetVersionNumber called");
        }

        // Special case: version call uses DirectRegister to set AX, BX, DX directly
        ushort ax = XmsVersion;        // XMS version 3.00
        ushort bx = XmsInternalVersion; // Internal revision
        ushort dx = 0x0000;            // No HMA available

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS GetVersionNumber: Version={0:X4}, Internal={1:X4}, HMA=No",
                ax, bx);
        }

        return XmsResult.DirectRegister(ax, bx, dx);
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
    public XmsResult RequestHighMemoryArea() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS RequestHighMemoryArea called with size={Size:X4}h", _state.DX);
        }

        // HMA exists but is in use by DOS
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("XMS RequestHighMemoryArea failed: HMA already in use");
        }

        return XmsResult.Error(XmsErrorCodes.HmaInUse);
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
    public XmsResult ReleaseHighMemoryArea() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS ReleaseHighMemoryArea called");
        }

        // Can't release HMA
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("XMS ReleaseHighMemoryArea failed: HMA not allocated to caller");
        }

        return XmsResult.Error(XmsErrorCodes.HmaNotAllocated);
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
    public XmsResult GlobalEnableA20() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS GlobalEnableA20 called, current A20 state={CurrentState}",
                _a20Gate.IsEnabled);
        }

        _a20GlobalEnabled = true;
        SetA20(true);

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS GlobalEnableA20 succeeded, A20={State}, GlobalEnabled={Global}, LocalCount={LocalCount}",
                _a20Gate.IsEnabled, _a20GlobalEnabled, _a20LocalEnableCount);
        }

        return XmsResult.CreateSuccess();
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
    public XmsResult GlobalDisableA20() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS GlobalDisableA20 called, current A20 state={CurrentState}, LocalCount={LocalCount}",
                _a20Gate.IsEnabled, _a20LocalEnableCount);
        }

        _a20GlobalEnabled = false;
        if (_a20LocalEnableCount == 0) {
            SetA20(false);

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("XMS GlobalDisableA20 succeeded, A20={State}", _a20Gate.IsEnabled);
            }

            return XmsResult.CreateSuccess();
        } else {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS GlobalDisableA20 failed: A20 still enabled by {Count} local calls",
                    _a20LocalEnableCount);
            }

            return XmsResult.Error(XmsErrorCodes.A20StillEnabled);
        }
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
    public XmsResult EnableLocalA20() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS LocalEnableA20 called, current count={CurrentCount}",
                _a20LocalEnableCount);
        }

        // Counter overflow protection
        if (_a20LocalEnableCount == A20MaxTimesEnabled) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error("XMS LocalEnableA20 failed: Counter overflow");
            }

            return XmsResult.Error(XmsErrorCodes.A20Error);
        }

        // Only enable A20 if count is 0
        if (_a20LocalEnableCount++ == 0) {
            SetA20(true);

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("XMS LocalEnableA20 physically enabled A20 line");
            }
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS LocalEnableA20 succeeded, new count={NewCount}, A20={State}",
                _a20LocalEnableCount, _a20Gate.IsEnabled);
        }

        return XmsResult.CreateSuccess();
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
    public XmsResult DisableLocalA20() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS LocalDisableA20 called, current count={CurrentCount}",
                _a20LocalEnableCount);
        }

        if (_a20LocalEnableCount == 0) {
            // A20 is not locally enabled, so can't be disabled
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS LocalDisableA20 failed: A20 not locally enabled");
            }

            return XmsResult.Error(XmsErrorCodes.A20Error);
        }

        // Decrement count and check if we can disable A20
        _a20LocalEnableCount--;

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS LocalDisableA20 decremented count to {NewCount}",
                _a20LocalEnableCount);
        }

        if (_a20LocalEnableCount == 0 && !_a20GlobalEnabled) {
            // No local enables and no global enable
            SetA20(false);

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("XMS LocalDisableA20 physically disabled A20 line");
            }
        } else if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS LocalDisableA20: A20 remains enabled (GlobalEnabled={Global}, LocalCount={LocalCount})",
                _a20GlobalEnabled, _a20LocalEnableCount);
        }

        return XmsResult.CreateSuccess();
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
    public XmsResult QueryA20() {
        bool isEnabled = IsA20Enabled();

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS QueryA20 returned: A20={State}, GlobalEnabled={Global}, LocalCount={LocalCount}",
                isEnabled, _a20GlobalEnabled, _a20LocalEnableCount);
        }

        // Special case: returns 1 in AX for enabled, 0 for disabled
        return XmsResult.DirectRegister(isEnabled ? (ushort)1 : (ushort)0);
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
    public XmsResult QueryFreeExtendedMemory() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS QueryFreeExtendedMemory called: LargestFreeBlock={LargestFree}KB, TotalFree={TotalFree}KB",
                LargestFreeBlock / 1024, TotalFreeMemory / 1024);
        }

        // Calculate sizes in KB
        ushort largestKB, totalKB;

        if (LargestFreeBlock <= ushort.MaxValue * 1024u) {
            largestKB = (ushort)(LargestFreeBlock / 1024u);
        } else {
            largestKB = ushort.MaxValue;
        }

        if (TotalFreeMemory <= ushort.MaxValue * 1024u) {
            totalKB = (ushort)(TotalFreeMemory / 1024);
        } else {
            totalKB = ushort.MaxValue;
        }

        if (largestKB == 0 && totalKB == 0) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS QueryFreeExtendedMemory: All memory is allocated");
            }
            return XmsResult.Error(XmsErrorCodes.XmsOutOfSpace);
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS QueryFreeExtendedMemory returned: Largest={Largest}KB, Total={Total}KB",
                largestKB, totalKB);
        }

        return XmsResult.CreateSuccess(largestKB, tertiaryValue: totalKB);
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
    public XmsResult AllocateExtendedMemoryBlock() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS AllocateExtendedMemoryBlock called: Size={SizeKB}KB", _state.DX);
        }

        byte res = TryAllocate(_state.DX * 1024u, out short handle);

        if (res == 0) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("XMS AllocateExtendedMemoryBlock succeeded: Handle={Handle:X4}h for {Size}KB",
                    handle, _state.DX);
            }
            return XmsResult.CreateSuccess((ushort)handle);
        } else {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS AllocateExtendedMemoryBlock failed: Error={Error:X2}h for {Size}KB",
                    res, _state.DX);
            }
            return XmsResult.Error((XmsErrorCodes)res);
        }
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
    public XmsResult FreeExtendedMemoryBlock() {
        int handle = _state.DX;

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS FreeExtendedMemoryBlock called: Handle={Handle:X4}h", handle);
        }

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS FreeExtendedMemoryBlock failed: Invalid handle {Handle:X4}h", handle);
            }
            return XmsResult.Error(XmsErrorCodes.XmsInvalidHandle);
        }

        if (lockCount > 0) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS FreeExtendedMemoryBlock failed: Block is locked {LockCount} times", lockCount);
            }
            return XmsResult.Error(XmsErrorCodes.XmsBlockLocked);
        }

        if (TryGetBlock(handle, out XmsBlock block)) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("XMS FreeExtendedMemoryBlock: Freeing block at offset {Offset:X8}h, length {Length:X8}h ({LengthKB}KB)",
                    block.Offset, block.Length, block.Length / 1024);
            }

            XmsBlock freeBlock = block.Free();
            _xmsBlocksLinkedList.Replace(block, freeBlock);
            MergeFreeBlocks(freeBlock);
        }

        _xmsHandles.Remove(handle);

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS FreeExtendedMemoryBlock succeeded for handle {Handle:X4}h", handle);
        }

        return XmsResult.CreateSuccess();
    }

    public XmsResult QueryAnyFreeExtendedMemory() {
        byte result = TryGetFreeMemoryInfo(out uint largestFree, out uint _);
        uint largestFreeKb = largestFree / 1024u;

        if (result == 0) {
            return XmsResult.CreateSuccessExtended(largestFreeKb);
        } else {
            return XmsResult.Error(XmsErrorCodes.XmsOutOfSpace);
        }
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
    public XmsResult MoveExtendedMemoryBlock() {
        bool a20State = IsA20Enabled();
        SetA20(true);

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS MoveExtendedMemoryBlock called, structure at DS:SI={DS:X4}h:{SI:X4}h",
                _state.DS, _state.SI);
        }

        uint address = MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI);
        var move = new ExtendedMemoryMoveStructure(_memory, address);

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS MoveExtendedMemoryBlock: Length={Length} bytes, Source={SrcHandle:X4}h:{SrcOffset:X8}h, Dest={DestHandle:X4}h:{DestOffset:X8}h",
                move.Length, move.SourceHandle, move.SourceOffset, move.DestHandle, move.DestOffset);
        }

        // "Length must be even" per XMS spec
        if (move.Length % 2 != 0) {
            SetA20(a20State);

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS MoveExtendedMemoryBlock failed: Length {Length} not even",
                    move.Length);
            }

            return XmsResult.Error(XmsErrorCodes.XmsParityError);
        }

        // Validate source
        uint srcAddress;
        if (move.SourceHandle == 0) {
            srcAddress = move.SourceAddress.Linear;
            if (srcAddress + move.Length > 0x10FFF0) {
                SetA20(a20State);

                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _loggerService.Warning("XMS MoveExtendedMemoryBlock failed: Source conventional memory address {Addr:X8}h + length exceeds limit",
                        srcAddress);
                }

                return XmsResult.Error(XmsErrorCodes.XmsInvalidLength);
            }
        } else if (TryGetBlock(move.SourceHandle, out XmsBlock srcBlock)) {
            if (move.SourceOffset + move.Length > srcBlock.Length) {
                SetA20(a20State);

                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _loggerService.Warning("XMS MoveExtendedMemoryBlock failed: Source offset {Offset:X8}h + length exceeds block size {Size:X8}h",
                        move.SourceOffset, srcBlock.Length);
                }

                return XmsResult.Error(XmsErrorCodes.XmsInvalidSrcOffset);
            }
            srcAddress = XmsBaseAddress + srcBlock.Offset + move.SourceOffset;

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("XMS MoveExtendedMemoryBlock: Source XMS block at physical address {Addr:X8}h", srcAddress);
            }
        } else {
            SetA20(a20State);

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS MoveExtendedMemoryBlock failed: Invalid source handle {Handle:X4}h",
                    move.SourceHandle);
            }

            return XmsResult.Error(XmsErrorCodes.XmsInvalidSrcHandle);
        }

        // Validate destination
        uint destAddress;
        if (move.DestHandle == 0) {
            destAddress = move.DestAddress.Linear;
            if (destAddress + move.Length > 0x10FFF0) {
                SetA20(a20State);

                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _loggerService.Warning("XMS MoveExtendedMemoryBlock failed: Destination conventional memory address {Addr:X8}h + length exceeds limit",
                        destAddress);
                }

                return XmsResult.Error(XmsErrorCodes.XmsInvalidLength);
            }
        } else if (TryGetBlock(move.DestHandle, out XmsBlock destBlock)) {
            if (move.DestOffset + move.Length > destBlock.Length) {
                SetA20(a20State);

                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _loggerService.Warning("XMS MoveExtendedMemoryBlock failed: Destination offset {Offset:X8}h + length exceeds block size {Size:X8}h",
                        move.DestOffset, destBlock.Length);
                }

                return XmsResult.Error(XmsErrorCodes.XmsInvalidDestOffset);
            }
            destAddress = XmsBaseAddress + destBlock.Offset + move.DestOffset;

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("XMS MoveExtendedMemoryBlock: Destination XMS block at physical address {Addr:X8}h", destAddress);
            }
        } else {
            SetA20(a20State);

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS MoveExtendedMemoryBlock failed: Invalid destination handle {Handle:X4}h",
                    move.DestHandle);
            }

            return XmsResult.Error(XmsErrorCodes.XmsInvalidDestHandle);
        }

        // Check for invalid overlap
        if (move.SourceHandle != 0 && move.SourceHandle == move.DestHandle) {
            uint srcStart = move.SourceOffset;
            uint srcEnd = srcStart + move.Length;
            uint destStart = move.DestOffset;
            uint destEnd = destStart + move.Length;
            if ((srcStart < destEnd) && (destStart < srcEnd)) {
                SetA20(a20State);

                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _loggerService.Warning("XMS MoveExtendedMemoryBlock failed: Invalid overlap between source and destination");
                }

                return XmsResult.Error(XmsErrorCodes.XmsInvalidOverlap);
            }
        }

        _memory.MemCopy(srcAddress, destAddress, move.Length);

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS MoveExtendedMemoryBlock succeeded: Moved {Length} bytes from {SrcAddr:X8}h to {DestAddr:X8}h",
                move.Length, srcAddress, destAddress);
        }

        SetA20(a20State);
        return XmsResult.CreateSuccess();
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
    public XmsResult UnlockExtendedMemoryBlock() {
        int handle = _state.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            return XmsResult.Error(XmsErrorCodes.XmsInvalidHandle);
        }

        if (lockCount < 1) {
            return XmsResult.Error(XmsErrorCodes.XmsBlockNotLocked);
        }

        _xmsHandles[handle] = lockCount - 1;
        return XmsResult.CreateSuccess();
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
    public XmsResult GetHandleInformation() {
        int handle = _state.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            return XmsResult.Error(XmsErrorCodes.XmsInvalidHandle);
        }

        byte freeHandles = (byte)(MaxHandles - _xmsHandles.Count);

        ushort sizeKb = 0;
        if (TryGetBlock(handle, out XmsBlock block)) {
            sizeKb = (ushort)(block.Length / 1024u);
        }

        return XmsResult.CreateSuccess((ushort)lockCount, freeHandles, sizeKb);
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
    /// <item>BL = A0h (out of space)</item>
    /// </list>
    /// </remarks>
    public XmsResult ReallocateExtendedMemoryBlock() {
        int handle = _state.DX;
        uint newSize = (uint)_state.BX * 1024u;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            return XmsResult.Error(XmsErrorCodes.XmsInvalidHandle);
        }

        if (lockCount > 0) {
            return XmsResult.Error(XmsErrorCodes.XmsBlockLocked);
        }

        if (!TryGetBlock(handle, out XmsBlock block)) {
            return XmsResult.Error(XmsErrorCodes.XmsInvalidHandle);
        }

        if (newSize == block.Length) {
            // No change needed
            return XmsResult.CreateSuccess();
        }

        // Handle size 0 (free block)
        if (newSize == 0) {
            XmsBlock freeBlock = block.Free();
            _xmsBlocksLinkedList.Replace(block, freeBlock);
            MergeFreeBlocks(freeBlock);
            _xmsHandles.Remove(handle);
            return XmsResult.CreateSuccess();
        }

        // Try to shrink or grow
        if (newSize < block.Length) {
            // Split block and free remainder
            XmsBlock[] newBlocks = block.Allocate(handle, newSize);
            _xmsBlocksLinkedList.Replace(block, newBlocks);
            MergeFreeBlocks(newBlocks[1]);
            return XmsResult.CreateSuccess();
        } else {
            // Try to grow using next free block
            LinkedListNode<XmsBlock>? node = _xmsBlocksLinkedList.Find(block);
            if (node?.Next != null && node.Next.Value.IsFree) {
                uint combined = block.Length + node.Next.Value.Length;
                if (combined >= newSize) {
                    XmsBlock merged = block.Join(node.Next.Value);
                    _xmsBlocksLinkedList.Remove(node.Next);
                    XmsBlock[] newBlocks = merged.Allocate(handle, newSize);
                    _xmsBlocksLinkedList.Replace(block, newBlocks);
                    if (newBlocks.Length > 1) {
                        MergeFreeBlocks(newBlocks[1]);
                    }
                    return XmsResult.CreateSuccess();
                }
            }
            return XmsResult.Error(XmsErrorCodes.XmsOutOfSpace);
        }
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
    public XmsResult RequestUpperMemoryBlock() {
        return XmsResult.Error(XmsErrorCodes.UmbNoBlocksAvailable);
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
    public XmsResult ReleaseUpperMemoryBlock() {
        return XmsResult.Error(XmsErrorCodes.NotImplemented);
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
    public XmsResult LockExtendedMemoryBlock() {
        int handle = _state.DX;

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS LockExtendedMemoryBlock called: Handle={Handle:X4}h", handle);
        }

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS LockExtendedMemoryBlock failed: Invalid handle {Handle:X4}h", handle);
            }
            return XmsResult.Error(XmsErrorCodes.XmsInvalidHandle);
        }

        if (lockCount >= byte.MaxValue) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS LockExtendedMemoryBlock failed: Lock count overflow for handle {Handle:X4}h", handle);
            }
            return XmsResult.Error(XmsErrorCodes.XmsLockCountOverflow);
        }

        _xmsHandles[handle] = lockCount + 1;

        if (!TryGetBlock(handle, out XmsBlock block)) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS LockExtendedMemoryBlock failed: Block not found for handle {Handle:X4}h", handle);
            }
            return XmsResult.Error(XmsErrorCodes.XmsInvalidHandle);
        }

        uint fullAddress = XmsBaseAddress + block.Offset;
        ushort highWord = (ushort)(fullAddress >> 16);
        ushort lowWord = (ushort)(fullAddress & 0xFFFFu);

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS LockExtendedMemoryBlock succeeded: Handle={Handle:X4}h, Address={Addr:X8}h, NewLockCount={LockCount}",
                handle, fullAddress, lockCount + 1);
        }

        return XmsResult.CreateSuccess(highWord, lowWord);
    }

    private void SetA20(bool enable) {
        bool oldState = _a20Gate.IsEnabled;
        _a20Gate.IsEnabled = enable;
        if (oldState != enable) {
            _loggerService.Information("A20 line state changed from {0} to {1}", oldState, enable);
        }
    }

    private bool IsA20Enabled() {
        bool enabled = _a20Gate.IsEnabled;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS: A20 line state checked: {State}", enabled);
        }
        return enabled;
    }

    public byte TryAllocate(uint length, out short handle) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS TryAllocate called: RequestedLength={Length} bytes", length);
        }
        
        handle = (short)GetNextHandle();
        if (handle == 0) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS TryAllocate failed: All {MaxHandles} handles in use", MaxHandles);
            }
            return 0xA1; // All handles are used.
        }

        // Round up to next kbyte if necessary.
        uint originalLength = length;
        if (length % 1024 != 0) {
            length = (length & 0xFFFFFC00u) + 1024u;
            
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("XMS TryAllocate: Rounding up from {Original} to {Rounded} bytes", 
                    originalLength, length);
            }
        } else {
            length &= 0xFFFFFC00u;
        }

        // Zero-length allocations are allowed.
        if (length == 0) {
            _xmsHandles.Add(handle, 0);
            
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("XMS TryAllocate: Allocated zero-length block with handle {Handle:X4}h", handle);
            }
            return 0;
        }

        XmsBlock? smallestFreeBlock = GetFreeBlocks()
            .Where(b => b.Length >= length)
            .Select(static b => new XmsBlock?(b))
            .FirstOrDefault();

        if (smallestFreeBlock == null) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS TryAllocate failed: No free blocks large enough for {Length} bytes (largest={Largest} bytes)", 
                    length, LargestFreeBlock);
            }
            return 0xA0; // Not enough free memory.
        }

        LinkedListNode<XmsBlock>? freeNode = _xmsBlocksLinkedList.Find(smallestFreeBlock.Value);
        if (freeNode is not null) {
            XmsBlock[] newNodes = freeNode.Value.Allocate(handle, length);
            _xmsBlocksLinkedList.Replace((XmsBlock)smallestFreeBlock, newNodes);
            
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("XMS TryAllocate: Allocated block of {Length} bytes from free block of {FreeLength} bytes, handle={Handle:X4}h", 
                    length, smallestFreeBlock.Value.Length, handle);
                
                if (newNodes.Length > 1) {
                    _loggerService.Verbose("XMS TryAllocate: Created remainder free block of {RemainderLength} bytes", 
                        newNodes[1].Length);
                }
            }
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

    public void MergeFreeBlocks(XmsBlock firstBlock) {
        if (!firstBlock.IsFree) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS MergeFreeBlocks called with non-free block at offset {Offset:X8}h", firstBlock.Offset);
            }
            return;
        }
        
        LinkedListNode<XmsBlock>? firstNode = _xmsBlocksLinkedList.Find(firstBlock);
        if (firstNode?.Next == null) {
            return;
        }
        
        LinkedListNode<XmsBlock> nextNode = firstNode.Next;
        if (!nextNode.Value.IsFree) {
            return;
        }
        
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS MergeFreeBlocks: Merging blocks at {Offset1:X8}h ({Length1} bytes) and {Offset2:X8}h ({Length2} bytes)", 
                firstBlock.Offset, firstBlock.Length, nextNode.Value.Offset, nextNode.Value.Length);
        }
        
        XmsBlock newBlock = firstBlock.Join(nextNode.Value);
        _xmsBlocksLinkedList.Remove(nextNode);
        _xmsBlocksLinkedList.Replace(firstBlock, newBlock);
        
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS MergeFreeBlocks: Created merged free block at {Offset:X8}h ({Length} bytes)", 
                newBlock.Offset, newBlock.Length);
        }
    }

    public XmsResult AllocateAnyExtendedMemory() {
        uint kbytes = _state.EDX;

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("XMS AllocateAnyExtendedMemory called: Size={SizeKB}KB", kbytes);
        }

        byte res = TryAllocate(kbytes * 1024u, out short handle);
        if (res == 0) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("XMS AllocateAnyExtendedMemory succeeded: Handle={Handle:X4}h for {Size}KB",
                    handle, kbytes);
            }
            return XmsResult.CreateSuccess((ushort)handle);
        } else {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("XMS AllocateAnyExtendedMemory failed: Error={Error:X2}h for {Size}KB",
                    res, kbytes);
            }
            return XmsResult.Error((XmsErrorCodes)res);
        }
    }

    public XmsResult GetExtendedEmbHandle() {
        int handle = _state.DX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            return XmsResult.Error(XmsErrorCodes.XmsInvalidHandle);
        }

        ushort freeHandles = (ushort)(MaxHandles - _xmsHandles.Count);

        if (!TryGetBlock(handle, out XmsBlock block)) {
            return XmsResult.CreateSuccess((ushort)lockCount, freeHandles);
        }

        // Return 32-bit block length
        uint blockLengthKb = block.Length / 1024u;

        return new XmsResult(
            true,
            0,
            (ushort)lockCount,
            freeHandles,
            0,
            blockLengthKb);
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
        return XmsRam.GetSpan((int)(address - XmsBaseAddress), length);
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

    /// <summary>
    /// XMS Function 88h: Query Any Free Extended Memory.
    /// Returns 32-bit sizes and highest address.
    /// </summary>
    /// <param name="largestFree">Largest free block in bytes.</param>
    /// <param name="totalFree">Total free memory in bytes.</param>
    /// <returns>Zero on success, nonzero on failure.</returns>
    private byte TryGetFreeMemoryInfo(out uint largestFree, out uint totalFree) {
        largestFree = LargestFreeBlock;
        totalFree = (uint)TotalFreeMemory;
        return largestFree > 0 ? (byte)0 : (byte)XmsErrorCodes.XmsOutOfSpace;
    }

    /// <summary>
    /// XMS Function 8Fh: Reallocate Any Extended Memory.
    /// Changes the size of an unlocked extended memory block using 32-bit size.
    /// </summary>
    public XmsResult ReallocateAnyExtendedMemory() {
        int handle = _state.DX;
        uint newSize = _state.EDX;

        if (!_xmsHandles.TryGetValue(handle, out int lockCount)) {
            return XmsResult.Error(XmsErrorCodes.XmsInvalidHandle);
        }

        if (lockCount > 0) {
            return XmsResult.Error(XmsErrorCodes.XmsBlockLocked);
        }

        if (!TryGetBlock(handle, out XmsBlock block)) {
            return XmsResult.Error(XmsErrorCodes.XmsInvalidHandle);
        }

        if (newSize == block.Length) {
            // No change needed
            return XmsResult.CreateSuccess();
        }

        // Handle size 0 (free block)
        if (newSize == 0) {
            XmsBlock freeBlock = block.Free();
            _xmsBlocksLinkedList.Replace(block, freeBlock);
            MergeFreeBlocks(freeBlock);
            _xmsHandles.Remove(handle);
            return XmsResult.CreateSuccess();
        }

        // Try to shrink or grow - same logic as ReallocateExtendedMemoryBlock but with 32-bit sizes
        if (newSize < block.Length) {
            // Split block and free remainder
            XmsBlock[] newBlocks = block.Allocate(handle, newSize);
            _xmsBlocksLinkedList.Replace(block, newBlocks);
            MergeFreeBlocks(newBlocks[1]);
            return XmsResult.CreateSuccess();
        } else {
            // Try to grow using next free block
            LinkedListNode<XmsBlock>? node = _xmsBlocksLinkedList.Find(block);
            if (node?.Next != null && node.Next.Value.IsFree) {
                uint combined = block.Length + node.Next.Value.Length;
                if (combined >= newSize) {
                    XmsBlock merged = block.Join(node.Next.Value);
                    _xmsBlocksLinkedList.Remove(node.Next);
                    XmsBlock[] newBlocks = merged.Allocate(handle, newSize);
                    _xmsBlocksLinkedList.Replace(block, newBlocks);
                    if (newBlocks.Length > 1) {
                        MergeFreeBlocks(newBlocks[1]);
                    }
                    return XmsResult.CreateSuccess();
                }
            }
            return XmsResult.Error(XmsErrorCodes.XmsOutOfSpace);
        }
    }
}