namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

using Spice86.Core;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Enums;
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
/// Implements the eXtended Memory Specification (XMS) version 3.0 for DOS applications.
/// </summary>
/// <remarks>
/// <para>
/// XMS provides DOS programs with access to memory above the conventional 640KB limit in 
/// Intel 80286 and 80386 based machines. It defines a standardized API for accessing:
/// </para>
/// <list type="bullet">
///   <item>
///     <term>High Memory Area (HMA)</term>
///     <description>The first 64KB-16bytes of extended memory (FFFF:0010 to FFFF:FFFF)</description>
///   </item>
///   <item>
///     <term>Extended Memory Blocks (EMBs)</term>
///     <description>Memory blocks above 1MB+64KB that can be allocated, locked, moved, and freed</description>
///   </item>
///   <item>
///     <term>Upper Memory Blocks (UMBs)</term>
///     <description>Optional memory blocks in the upper memory area (640KB-1MB). <br/> 
///     As the specs do not require them to be implemented, they are not supported by this implementation.<br/></description>
///   </item>
/// </list>
/// <para>
/// XMS functions are accessed via INT 2Fh, AH=43h, and use the concept of "handles" to reference
/// allocated memory blocks. This implementation supports all required XMS 3.0 functions including
/// the 32-bit extended functions (88h, 89h, 8Eh, 8Fh) that support memory access beyond 64MB.
/// </para>
/// <para>
/// System Memory layout with XMS and UMBs:
/// <code>
///  _______________________________________   Top of Memory
/// |                                       |
/// |    Extended Memory Blocks (EMBs)      |
/// |    (Data storage only)                |
/// |_______________________________________|   1088KB
/// |                                       |
/// |    High Memory Area (HMA)             |
/// |    (64KB-16bytes)                     |
/// |=======================================|   1024KB (1MB)
/// |                                       |
/// |    Upper Memory Blocks (UMBs)         |
/// |    (Optional)                         |
/// |_______________________________________|   640KB
/// |                                       |
/// |    Conventional DOS Memory            |
/// |                                       |
/// |_______________________________________|   0KB
/// </code>
/// </para>
/// <para>
/// This implementation is equivalent to HIMEM.SYS in MS-DOS, providing memory management
/// services and A20 line control for DOS applications running in the emulator.
/// </para>
/// </remarks>
public sealed class ExtendedMemoryManager : IVirtualDevice, IMemoryDevice {
    /// <summary>
    /// The XMS version number in BCD format (0x0300 = version 3.00).
    /// </summary>
    /// <remarks>
    /// This is returned by Function 00h (Get XMS Version Number) in the AX register.
    /// XMS 3.0 adds extended functions that support memory beyond 64MB limit.
    /// </remarks>
    public const ushort XmsVersion = 0x0300;

    /// <summary>
    /// The internal revision number of the XMS driver.
    /// </summary>
    /// <remarks>
    /// This is returned by Function 00h (Get XMS Version Number) in the BX register.
    /// It's mainly used for debugging purposes as per the XMS specification.
    /// </remarks>
    public const ushort XmsInternalVersion = 0x0301;

    /// <summary>
    /// Flag indicating if the A20 line is globally enabled.
    /// </summary>
    /// <remarks>
    /// The global A20 state is controlled by Functions 03h (Global Enable A20) and 
    /// 04h (Global Disable A20). This flag is separate from the local enable count
    /// and is typically used by programs that have control of the HMA.
    /// </remarks>
    private bool _a20GlobalEnabled = false;

    /// <summary>
    /// Counter for local A20 enable/disable calls.
    /// </summary>
    /// <remarks>
    /// Per XMS specification, the A20 line should be controlled via an "enable count".
    /// Local Enable (Function 05h) increments this count and enables A20 if the count was zero.
    /// Local Disable (Function 06h) decrements this count and disables A20 if the count becomes zero.
    /// This allows nested A20 enables/disables to work correctly.
    /// </remarks>
    private uint _a20LocalEnableCount = 0;

    /// <summary>
    /// Maximum value for the A20 local enable count to prevent overflow.
    /// </summary>
    /// <remarks>
    /// When the local enable count reaches this value, further attempts to 
    /// enable A20 locally will return error code 82h (A20 error).
    /// </remarks>
    private const uint A20MaxTimesEnabled = uint.MaxValue;

    /// <summary>
    /// Logger service for recording XMS operations and errors.
    /// </summary>
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// CPU state containing registers used for XMS function calls and returns.
    /// </summary>
    /// <remarks>
    /// XMS functions take arguments in CPU registers and return results in registers:
    /// - AH: Contains the XMS function code (e.g., 00h, 09h, 0Ch)
    /// - AX: Return value, 0001h (success) or 0000h (failure)
    /// - BL: Error code on failure
    /// - Other registers: Function-specific parameters and return values
    /// </remarks>
    private readonly State _state;

    /// <summary>
    /// Controller for the A20 address line.
    /// </summary>
    /// <remarks>
    /// The A20 address line must be enabled to access memory above 1MB.
    /// XMS provides functions to control the A20 line state both globally and locally.
    /// </remarks>
    private readonly A20Gate _a20Gate;

    /// <summary>
    /// Memory bus for reading/writing XMS memory.
    /// </summary>
    private readonly IMemory _memory;

    /// <summary>
    /// Linked list of XMS memory blocks (free and allocated).
    /// </summary>
    /// <remarks>
    /// This data structure tracks all XMS memory blocks. Each block has a handle (for allocated blocks),
    /// offset, length, and free/allocated status. The linked list structure allows for efficient
    /// merging of adjacent free blocks to reduce fragmentation.
    /// </remarks>
    private readonly LinkedList<XmsBlock> _xmsBlocksLinkedList = new();

    /// <summary>
    /// Maps XMS handles to their lock counts.
    /// </summary>
    /// <remarks>
    /// Each allocated XMS block has a handle and a lock count. The lock count tracks how many times
    /// Function 0Ch (Lock Extended Memory Block) has been called on the block without a matching
    /// Function 0Dh (Unlock Extended Memory Block) call. A block with a non-zero lock count
    /// cannot be freed or reallocated.
    /// </remarks>
    private readonly SortedList<int, int> _xmsHandles = new();

    /// <summary>
    /// The segment of the XMS DOS Device Driver in memory.
    /// </summary>
    /// <remarks>
    /// In a real DOS system, HIMEM.SYS would be loaded as a device driver at this segment.
    /// The device driver header would be located at this address, followed by the driver code.
    /// </remarks>
    public const ushort DosDeviceSegment = MemoryMap.DeviceDriversSegment;

    /// <summary>
    /// The size of available XMS Memory, in kilobytes.
    /// </summary>
    /// <remarks>
    /// This implementation provides 8MB of XMS memory. The XMS 2.0 specification technically
    /// limited extended memory to 64MB due to using 16-bit values for sizes in KB.
    /// XMS 3.0 added functions (88h, 89h, 8Eh, 8Fh) that use 32-bit values for sizes in bytes,
    /// allowing access to memory beyond the 64MB limit.
    /// </remarks>
    public const uint XmsMemorySize = 8 * 1024;

    /// <summary>
    /// The starting physical address of XMS memory.
    /// </summary>
    /// <remarks>
    /// XMS blocks start at 1088KB (1MB + 64KB), after the High Memory Area (HMA).
    /// This address (0x10FFF0) corresponds to linear address 1088KB - 16 bytes.
    /// </remarks>
    public const uint XmsBaseAddress = 0x10FFF0;

    /// <summary>
    /// Maximum number of XMS handles that can be allocated simultaneously.
    /// </summary>
    /// <remarks>
    /// This is the default value from HIMEM.SYS. In a real DOS system, this value could be
    /// adjusted via the /NUMHANDLES= parameter in CONFIG.SYS. Each handle consumes a small
    /// amount of conventional memory, so this value represents a tradeoff between the number
    /// of allocatable blocks and conventional memory usage.
    /// </remarks>
    private const int MaxHandles = 128;

    /// <summary>
    /// XMS plain old memory.
    /// </summary>
    /// <remarks>
    /// This is the actual memory buffer that holds the XMS memory content.
    /// It's sized according to XmsMemorySize, which is 8MB in this implementation.
    /// </remarks>
    public Ram XmsRam { get; private set; } = new(XmsMemorySize * 1024);

    /// <summary>
    /// DOS Device Driver Name.
    /// </summary>
    /// <remarks>
    /// This is the device name that would appear in the DOS device driver chain.
    /// Real HIMEM.SYS uses "XMSXXXX0" as its device name.
    /// </remarks>
    public const string XmsIdentifier = "XMSXXXX0";

    /// <summary>
    /// The memory address to the C# XMS callback.
    /// </summary>
    /// <remarks>
    /// This is the address where the XMS multiplexer interrupt handler (INT 2Fh, AH=43h)
    /// jumps to when calling the XMS API. It's set up to execute the <see cref="RunMultiplex"/> method.
    /// </remarks>
    public SegmentedAddress CallbackAddress { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedMemoryManager"/> class.
    /// </summary>
    /// <param name="memory">The memory bus for accessing emulated memory.</param>
    /// <param name="a20Gate">The A20 gate controller for managing the A20 line state.</param>
    /// <param name="state">The CPU state for accessing registers during XMS operations.</param>
    /// <param name="memoryAsmWriter">Helper for writing assembly code to memory.</param>
    /// <param name="dosTables">DOS memory tables for placing the XMS driver in memory.</param>
    /// <param name="loggerService">The logger service for recording XMS operations.</param>
    /// <remarks>
    /// This constructor:
    /// <list type="bullet">
    /// <item>Creates a DOS device header for the XMS driver</item>
    /// <item>Sets up a callback for the INT 2Fh XMS multiplex handler</item>
    /// <item>Initializes the XMS memory area as a single free block</item>
    /// <item>Registers memory mappings for both XMS memory and HMA</item>
    /// <item>Enables the A20 line by default</item>
    /// </list>
    /// </remarks>
    public ExtendedMemoryManager(IMemory memory, State state, A20Gate a20Gate,
        MemoryAsmWriter memoryAsmWriter, DosTables dosTables,
        ILoggerService loggerService) {
        uint headerAddress = MemoryUtils.ToPhysicalAddress(DosDeviceSegment, 0);
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
        var hookableCodeAddress = new SegmentedAddress((ushort)(dosTables
            .GetDosPrivateTableWritableAddress(0x1) - 1), 0x10);
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
        Name = XmsIdentifier;

        // Initialize XMS memory as a single free block
        _xmsBlocksLinkedList.AddLast(new XmsBlock(0, 0, XmsMemorySize * 1024, true));
    }

    /// <summary>
    /// Gets the largest free block of memory in bytes.
    /// </summary>
    /// <remarks>
    /// This property is used by Functions 08h (Query Free Extended Memory) and 88h (Query Any Free Extended Memory)
    /// to report the size of the largest available memory block. It's calculated by finding the largest
    /// free block in the XMS memory pool.
    /// </remarks>
    public uint LargestFreeBlock => GetFreeBlocks().FirstOrDefault().Length;

    /// <summary>
    /// Gets the total amount of free memory in bytes.
    /// </summary>
    /// <remarks>
    /// This property is used by Functions 08h (Query Free Extended Memory) and 88h (Query Any Free Extended Memory)
    /// to report the total free memory available. It's calculated by summing the sizes of all free blocks
    /// in the XMS memory pool.
    /// </remarks>
    public long TotalFreeMemory => GetFreeBlocks().Sum(b => b.Length);

    /// <summary>
    /// Dispatches XMS subfunctions based on the value in AH register.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the main entry point for XMS API calls via the multiplex interrupt INT 2Fh, AH=43h.
    /// It examines the AH register to determine which XMS function to execute, calls the appropriate
    /// method, and then applies the results to the CPU registers according to the XMS specification.
    /// </para>
    /// <para>
    /// XMS functions follow this general pattern:
    /// <list type="bullet">
    /// <item>Input parameters are passed in CPU registers</item>
    /// <item>Function execution produces an XmsResult structure</item>
    /// <item>On success (AX=0001h), additional results are placed in various registers</item>
    /// <item>On failure (AX=0000h), an error code is placed in BL</item>
    /// </list>
    /// </para>
    /// <para>
    /// The method handles all standard XMS functions (00h-12h) as well as the extended
    /// 32-bit functions (88h, 89h, 8Eh, 8Fh) introduced in XMS 3.0.
    /// </para>
    /// </remarks>
    public void RunMultiplex() {
        var operation = (XmsSubFunctionsCodes)_state.AH;

        if(_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("XMS call from CS:IP={CS:X4}:{IP:X4}, function {Function:X2}h",
                _state.CS, _state.IP, _state.AH);
        }

        // Log detailed diagnostics for each XMS call
        string functionName = operation.ToString();
        string parameters;
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

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("XMS CALL: {Function} ({FuncCode:X2}h) with {Parameters} - CS:IP={CS:X4}:{IP:X4}",
            functionName, (byte)operation, parameters, _state.CS, _state.IP);
        }

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
            _state.AX = 1;  // AX=0001h indicates success
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
            _state.AX = 0;  // AX=0000h indicates failure
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
    /// <para>
    /// This function identifies the XMS version and driver capabilities. It's often
    /// the first XMS function called by applications to verify XMS presence and version.
    /// </para>
    /// <para>
    /// Register inputs:
    /// <list type="bullet">
    /// <item>AH = 00h (Function code)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Register outputs:
    /// <list type="bullet">
    /// <item>AX = XMS version number as BCD (0300h = version 3.00)</item>
    /// <item>BX = Driver internal revision number (implementation-specific)</item>
    /// <item>DX = 0001h if HMA exists, 0000h otherwise</item>
    /// </list>
    /// </para>
    /// <para>
    /// This function never fails and doesn't change the A20 line state.
    /// </para>
    /// </remarks>
    /// <returns>An XmsResult structure with version information.</returns>
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
    /// <para>
    /// The High Memory Area is the first 64KB minus 16 bytes of extended memory (FFFF:0010 to FFFF:FFFF).
    /// It's unique because it can be accessed in real mode after enabling the A20 line.
    /// Only one program can use the HMA at a time.
    /// </para>
    /// <para>
    /// Register inputs:
    /// <list type="bullet">
    /// <item>AH = 01h (Function code)</item>
    /// <item>DX = Bytes needed in HMA for TSRs/drivers, or FFFFh for applications</item>
    /// </list>
    /// </para>
    /// <para>
    /// Register outputs:
    /// <list type="bullet">
    /// <item>AX = 0001h if successful, 0000h if failed</item>
    /// <item>If failed, BL = Error code</item>
    /// </list>
    /// </para>
    /// <para>
    /// Possible errors:
    /// <list type="bullet">
    /// <item>90h - HMA does not exist</item>
    /// <item>91h - HMA is already in use</item>
    /// <item>92h - DX &lt; /HMAMIN= parameter</item>
    /// </list>
    /// </para>
    /// <para>
    /// In this implementation, the HMA is not available for allocation because it's considered to be
    /// already in use by the system (simulating DOS 5+ which can load parts of itself into the HMA).
    /// </para>
    /// </remarks>
    /// <returns>An XmsResult structure indicating whether the HMA was assigned.</returns>
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
    /// <para>
    /// This function releases control of the HMA so other programs can use it.
    /// Programs must release the HMA before exiting.
    /// </para>
    /// <para>
    /// Register inputs:
    /// <list type="bullet">
    /// <item>AH = 02h (Function code)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Register outputs:
    /// <list type="bullet">
    /// <item>AX = 0001h if released, 0000h if failed</item>
    /// <item>If failed, BL = Error code</item>
    /// </list>
    /// </para>
    /// <para>
    /// Possible errors:
    /// <list type="bullet">
    /// <item>90h - HMA does not exist</item>
    /// <item>93h - HMA was not allocated to caller</item>
    /// </list>
    /// </para>
    /// <para>
    /// In this implementation, the HMA cannot be released because it was never allocated to
    /// the caller (it's considered to be in use by the system).
    /// </para>
    /// </remarks>
    /// <returns>An XmsResult structure indicating whether the HMA was released.</returns>
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
    /// <para>
    /// This function enables the A20 line to allow access to memory above 1MB, including the HMA.
    /// It should only be used by programs that have control of the HMA.
    /// </para>
    /// <para>
    /// Register inputs:
    /// <list type="bullet">
    /// <item>AH = 03h (Function code)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Register outputs:
    /// <list type="bullet">
    /// <item>AX = 0001h if enabled, 0000h if failed</item>
    /// <item>If failed, BL = Error code</item>
    /// </list>
    /// </para>
    /// <para>
    /// Possible errors:
    /// <list type="bullet">
    /// <item>82h - A20 error</item>
    /// </list>
    /// </para>
    /// <para>
    /// This function sets the global A20 enable flag and physically enables the A20 line.
    /// On many machines, toggling the A20 line is a relatively slow operation.
    /// </para>
    /// </remarks>
    /// <returns>An XmsResult structure indicating whether A20 was enabled.</returns>
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
    /// <para>
    /// This function disables the A20 line, preventing access to memory above 1MB.
    /// It should only be used by programs that have control of the HMA.
    /// </para>
    /// <para>
    /// Register inputs:
    /// <list type="bullet">
    /// <item>AH = 04h (Function code)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Register outputs:
    /// <list type="bullet">
    /// <item>AX = 0001h if disabled, 0000h if failed</item>
    /// <item>If failed, BL = Error code</item>
    /// </list>
    /// </para>
    /// <para>
    /// Possible errors:
    /// <list type="bullet">
    /// <item>82h - A20 error</item>
    /// <item>94h - A20 still enabled by local calls</item>
    /// </list>
    /// </para>
    /// <para>
    /// This function clears the global A20 enable flag and physically disables the A20 line
    /// if there are no local enables active. If local enables are active (non-zero local enable count),
    /// the A20 line remains enabled and error 94h is returned.
    /// </para>
    /// </remarks>
    /// <returns>An XmsResult structure indicating whether A20 was disabled.</returns>
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
    /// <para>
    /// This function enables the A20 line for direct access to extended memory and
    /// increments the local enable count. It should be balanced with a call to
    /// Function 06h (Local Disable A20) before program termination.
    /// </para>
    /// <para>
    /// Register inputs:
    /// <list type="bullet">
    /// <item>AH = 05h (Function code)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Register outputs:
    /// <list type="bullet">
    /// <item>AX = 0001h if enabled, 0000h if failed</item>
    /// <item>If failed, BL = Error code</item>
    /// </list>
    /// </para>
    /// <para>
    /// Possible errors:
    /// <list type="bullet">
    /// <item>82h - A20 error (counter overflow)</item>
    /// </list>
    /// </para>
    /// <para>
    /// The function maintains a counter of local A20 enable calls. Each call increments
    /// the counter, and the A20 line is enabled if the counter was zero. This allows
    /// for nested enables/disables to work correctly. If the counter would overflow,
    /// the function fails with error 82h.
    /// </para>
    /// </remarks>
    /// <returns>An XmsResult structure indicating whether A20 was locally enabled.</returns>
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
    /// <para>
    /// This function decrements the local A20 enable count and disables the A20 line
    /// if the count reaches zero and global A20 is not enabled. It balances a previous
    /// call to Function 05h (Local Enable A20).
    /// </para>
    /// <para>
    /// Register inputs:
    /// <list type="bullet">
    /// <item>AH = 06h (Function code)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Register outputs:
    /// <list type="bullet">
    /// <item>AX = 0001h if successful, 0000h if failed</item>
    /// <item>If failed, BL = Error code</item>
    /// </list>
    /// </para>
    /// <para>
    /// Possible errors:
    /// <list type="bullet">
    /// <item>82h - A20 error (not locally enabled)</item>
    /// <item>94h - A20 still enabled (by global enable)</item>
    /// </list>
    /// </para>
    /// <para>
    /// The function maintains a counter of local A20 enable calls. Each disable call decrements
    /// the counter, and the A20 line is disabled if the counter reaches zero and global A20
    /// is not enabled. If the counter is already zero (A20 not locally enabled), the function
    /// fails with error 82h.
    /// </para>
    /// </remarks>
    /// <returns>An XmsResult structure indicating whether A20 was successfully disabled locally.</returns>
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
    /// <para>
    /// This function tests the physical state of the A20 line to determine if it's enabled or disabled.
    /// It works by checking if "memory wrap" occurs, which is when addressing 1MB+X wraps to X
    /// when A20 is disabled.
    /// </para>
    /// <para>
    /// Register inputs:
    /// <list type="bullet">
    /// <item>AH = 07h (Function code)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Register outputs:
    /// <list type="bullet">
    /// <item>AX = 0001h if A20 is enabled, 0000h if disabled</item>
    /// <item>BL = 00h (success)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Possible errors: None
    /// </para>
    /// <para>
    /// Unlike most XMS functions, this function returns 0001h or 0000h in AX to indicate
    /// the A20 state, not the function success. BL is set to 00h to indicate the function
    /// succeeded in determining the A20 state.
    /// </para>
    /// </remarks>
    /// <returns>An XmsResult structure with the A20 state in the PrimaryValue field.</returns>
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
    /// <para>
    /// This function reports the amount of free extended memory available for allocation.
    /// It returns both the largest contiguous block and the total amount of free memory.
    /// </para>
    /// <para>
    /// Register inputs:
    /// <list type="bullet">
    /// <item>AH = 08h (Function code)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Register outputs:
    /// <list type="bullet">
    /// <item>AX = Size of largest free block in K-bytes</item>
    /// <item>DX = Total free extended memory in K-bytes</item>
    /// </list>
    /// </para>
    /// <para>
    /// Possible errors:
    /// <list type="bullet">
    /// <item>A0h - All extended memory is allocated</item>
    /// </list>
    /// </para>
    /// <para>
    /// The 64KB HMA is not included in the returned values even if it's not in use.
    /// This function is limited to reporting sizes up to 64MB due to using 16-bit registers
    /// for K-byte values. For larger memory pools, use Function 88h (Query Any Free Extended Memory).
    /// </para>
    /// </remarks>
    /// <returns>An XmsResult structure with the free memory information.</returns>
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
    /// <para>
    /// This function attempts to allocate a block of extended memory of the requested size
    /// and returns a handle to identify the block in subsequent XMS calls.
    /// </para>
    /// <para>
    /// Register inputs:
    /// <list type="bullet">
    /// <item>AH = 09h (Function code)</item>
    /// <item>DX = Size of memory to allocate in K-bytes</item>
    /// </list>
    /// </para>
    /// <para>
    /// Register outputs:
    /// <list type="bullet">
    /// <item>AX = 0001h if allocated, 0000h if failed</item>
    /// <item>DX = Handle to allocated block (if successful)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Possible errors:
    /// <list type="bullet">
    /// <item>A0h - All available extended memory is allocated</item>
    /// <item>A1h - All available extended memory handles are in use</item>
    /// </list>
    /// </para>
    /// <para>
    /// Extended memory handles are scarce resources (limited by MaxHandles). Programs should
    /// try to allocate as few blocks as possible. Allocation of a zero-length block is allowed
    /// and can be useful for reserving a handle. This function is limited to allocations up to
    /// 64MB due to using a 16-bit register for K-byte size. For larger allocations, use
    /// Function 89h (Allocate Any Extended Memory).
    /// </para>
    /// </remarks>
    /// <returns>An XmsResult structure with the allocation result.</returns>
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
    /// <para>
    /// This function releases an XMS memory block that was previously allocated using
    /// Function 09h (Allocate Extended Memory Block).
    /// </para>
    /// <para>
    /// Register inputs:
    /// <list type="bullet">
    /// <item>AH = 0Ah (Function code)</item>
    /// <item>DX = Handle of the block to free</item>
    /// </list>
    /// </para>
    /// <para>
    /// Register outputs:
    /// <list type="bullet">
    /// <item>AX = 0001h if freed, 0000h if failed</item>
    /// </list>
    /// </para>
    /// <para>
    /// Possible errors:
    /// <list type="bullet">
    /// <item>A2h - Invalid handle</item>
    /// <item>ABh - Block is locked</item>
    /// </list>
    /// </para>
    /// <para>
    /// Programs should free all allocated memory blocks before exiting. When a block is freed,
    /// its handle and data become invalid and should not be accessed. A block cannot be freed
    /// if it is locked (has a non-zero lock count).
    /// </para>
    /// </remarks>
    /// <returns>An XmsResult structure indicating whether the block was freed.</returns>
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

    /// <summary>
    /// XMS Function 88h: Query Any Free Extended Memory.
    /// Returns extended memory availability using 32-bit values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This 386+ specific function reports the amount of free extended memory using 32-bit values,
    /// allowing it to handle memory pools larger than 64MB. It returns the largest free block,
    /// total free memory, and the highest memory address.
    /// </para>
    /// <para>
    /// Register inputs:
    /// <list type="bullet">
    /// <item>AH = 88h (Function code)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Register outputs:
    /// <list type="bullet">
    /// <item>AX = 0001h if successful, 0000h if failed</item>
    /// <item>EDX:EAX = Size of largest free block in bytes (if successful)</item>
    /// <item>ECX:EBX = Total free memory in bytes (if successful)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Possible errors:
    /// <list type="bullet">
    /// <item>80h - Function not implemented (on 80286 machines)</item>
    /// <item>A0h - All extended memory is allocated</item>
    /// </list>
    /// </para>
    /// <para>
    /// This function is similar to Function 08h but returns 32-bit byte values instead of
    /// 16-bit K-byte values, allowing it to report memory sizes beyond the 64MB limit.
    /// It's only available on 80386 and higher processors.
    /// </para>
    /// </remarks>
    /// <returns>An XmsResult structure with the extended memory information.</returns>
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
    /// <para>
    /// This function transfers a block of data between memory locations based on the
    /// Extended Memory Move Structure. It can move data between conventional memory and
    /// extended memory, or within either area.
    /// </para>
    /// <para>
    /// Register inputs:
    /// <list type="bullet">
    /// <item>AH = 0Bh (Function code)</item>
    /// <item>DS:SI = Pointer to an Extended Memory Move Structure</item>
    /// </list>
    /// </para>
    /// <para>
    /// Extended Memory Move Structure:
    /// <list type="table">
    /// <item>
    ///   <term>Offset</term>
    ///   <term>Size</term>
    ///   <term>Description</term>
    /// </item>
    /// <item>
    ///   <term>00h</term>
    ///   <term>DWORD</term>
    ///   <term>Length of data to move in bytes</term>
    /// </item>
    /// <item>
    ///   <term>04h</term>
    ///   <term>WORD</term>
    ///   <term>Source handle (0 for conventional memory)</term>
    /// </item>
    /// <item>
    ///   <term>06h</term>
    ///   <term>DWORD</term>
    ///   <term>Source offset (or segment:offset if handle is 0)</term>
    /// </item>
    /// <item>
    ///   <term>0Ah</term>
    ///   <term>WORD</term>
    ///   <term>Destination handle (0 for conventional memory)</term>
    /// </item>
    /// <item>
    ///   <term>0Ch</term>
    ///   <term>DWORD</term>
    ///   <term>Destination offset (or segment:offset if handle is 0)</term>
    /// </item>
    /// </list>
    /// </para>
    /// <para>
    /// Register outputs:
    /// <list type="bullet">
    /// <item>AX = 0001h if successful, 0000h if failed</item>
    /// </list>
    /// </para>
    /// <para>
    /// Possible errors:
    /// <list type="bullet">
    /// <item>82h - A20 error</item>
    /// <item>A3h - Invalid source handle</item>
    /// <item>A4h - Invalid source offset</item>
    /// <item>A5h - Invalid destination handle</item>
    /// <item>A6h - Invalid destination offset</item>
    /// <item>A7h - Invalid length</item>
    /// <item>A8h - Invalid overlap</item>
    /// <item>A9h - Parity error</item>
    /// </list>
    /// </para>
    /// <para>
    /// If a handle is 0, the corresponding offset is interpreted as a segment:offset pair
    /// in conventional memory. The Length must be even. The function preserves the A20 line state
    /// and enables it temporarily during the operation if needed.
    /// </para>
    /// </remarks>
    /// <returns>An XmsResult structure indicating whether the move was successful.</returns>
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
            XmsBlock[] allocatedBlocks = freeNode.Value.Allocate(handle, length);
            _xmsBlocksLinkedList.Replace((XmsBlock)smallestFreeBlock, allocatedBlocks);

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("XMS TryAllocate: Allocated block of {Length} bytes from free block of {FreeLength} bytes, handle={Handle:X4}h",
                    length, smallestFreeBlock.Value.Length, handle);

                if (allocatedBlocks.Length > 1) {
                    _loggerService.Verbose("XMS TryAllocate: Created remainder free block of {RemainderLength} bytes",
                        allocatedBlocks[1].Length);
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
        block = _xmsBlocksLinkedList.FirstOrDefault(b => !b.IsFree && b.Handle == handle);
        return block != default;
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
        return XmsRam.Read(address);
    }

    /// <inheritdoc/>
    public void Write(uint address, byte value) {
        XmsRam.Write(address, value);
    }

    /// <inheritdoc/>
    public Span<byte> GetSpan(int address, int length) {
        return XmsRam.GetSpan(address, length);
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
    /// BIOS-compatible function to copy extended memory - but preserves local A20 gate state.
    /// Otherwise, it's the same as INT 15h, AH=87h.
    /// This is not a standard XMS function, but XMS is supposed to override INT15H, AH=87h according to specs.
    /// The copy parameters are passed in ES:SI in a <see cref="ExtendedMemoryMoveStructure"/>.
    /// </summary>
    public void CopyExtendedMemory() {
        bool a20WasEnabled = IsA20Enabled();
        SetA20(true);
        ushort numberOfWordsToCopy = _state.CX;
        uint globalDescriptorTableAddress = MemoryUtils.ToPhysicalAddress(
            _state.ES, _state.SI);
        var descriptor = new GlobalDescriptorTable(_memory,
            globalDescriptorTableAddress);
        _memory.MemCopy(descriptor.GetLinearSourceAddress(),
            descriptor.GetLinearDestAddress(),
            numberOfWordsToCopy);
        SetA20(a20WasEnabled);
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