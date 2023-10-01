namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// BIOS services
/// </summary>
public class SystemBiosInt15Handler : InterruptHandler {
    private readonly A20Gate _a20Gate;
    
    /// <summary>
    /// Set on startup, and stored in the CMOS battery.
    /// </summary>
    /// <remarks>This is 0 because either XMS is not installed, or 0 because the XMS driver has to protect the HMA.</remarks>
    private const byte ExtendedMemorySize = 0;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="extendedMemorySize">The number of 1K blocks above 1KB. Either 0 because XMS is not installed, or 0 to protect the HMA.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="a20Gate">The A20 line gate.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public SystemBiosInt15Handler(IMemory memory, Cpu cpu, A20Gate a20Gate, ILoggerService loggerService) : base(memory, cpu, loggerService) {
        _a20Gate = a20Gate;
        FillDispatchTable();
    }

    private void FillDispatchTable() {
        AddAction(0x24, () => ToggleA20GateOrGetStatus(true));
        AddAction(0x6, Unsupported);
        AddAction(0xC0, Unsupported);
        AddAction(0xC2, Unsupported);
        AddAction(0xC4, Unsupported);
        AddAction(0x87, () => CopyExtendedMemory(true));
        AddAction(0x88, () => GetExtendedMemorySize(true));
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x15;

    /// <inheritdoc />
    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    /// <summary>
    /// Copy extended memory (XMS), legacy function provided by the BIOS.
    /// </summary>
    /// <param name="calledFromVm">Whether the call comes from emulated machine code.</param>
    public void CopyExtendedMemory(bool calledFromVm) {
        bool enabled = _memory.A20Gate.IsEnabled;
        _memory.A20Gate.IsEnabled = true;
        //TODO: Make this a MemoryBasedStructure class
        //https://fd.lod.bz/rbil/interrup/bios/1587.html#table-00499
        // ES:SI points to descriptor table
        //  Format of global descriptor table:
        // Offset	Size	Description	(Table 00499)
        // ===========================================
        //  00h 16 BYTEs	zeros (used by BIOS)
        //  10h	WORD	source segment length in bytes (2*CX-1 or greater)
        //  12h  3 BYTEs	24-bit linear source address, low byte first
        //  15h	BYTE	source segment access rights (93h)
        //  16h	WORD	(286) zero
        // 		(386+) extended access rights and high byte of source address
        //  18h	WORD	destination segment length in bytes (2*CX-1 or greater)
        //  1Ah  3 BYTEs	24-bit linear destination address, low byte first
        //  1Dh	BYTE	destination segment access rights (93h)
        //  1Eh	WORD	(286) zero
        // 		(386+) extended access rights and high byte of destin. address
        //  20h 16 BYTEs	zeros (used by BIOS to build CS and SS descriptors)
        uint offset =  MemoryUtils.ToPhysicalAddress(_state.ES, _state.SI);
        uint sourceAddress = (uint)(_memory.UInt16[offset + 0x12] + _memory.UInt8[offset + 0x16]);
        uint destAddress = (uint)(_memory.UInt16[offset + 0x1A] + _memory.UInt8[offset + 0x1E]);
        uint length = (uint)(_state.CX * 2);
        _memory.MemCopy(sourceAddress, destAddress, length);
        _state.AX = 0x00;
        _memory.A20Gate.IsEnabled = enabled;
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Bios support function for the A20 Gate line. <br/>
    /// AL contains one of:
    /// <ul>
    ///   <li>0: Disable</li>
    ///   <li>1: Enable</li>
    ///   <li>2: Query status</li>
    ///   <li>3: Get A20 support</li>
    /// </ul>
    /// </summary>
    /// <param name="calledFromVm">Whether the call comes from emulated machine code.</param>
    public void ToggleA20GateOrGetStatus(bool calledFromVm) {
        switch (_state.AL) {
            case 0:
                _a20Gate.IsEnabled = false;
                SetCarryFlag(false, calledFromVm);
                break;
            case 1:
                _a20Gate.IsEnabled = true;
                SetCarryFlag(false, calledFromVm);
                break;
            case 2:
                _state.AL = (byte) (_a20Gate.IsEnabled ? 0x1 : 0x0);
                _state.AH = 0; // success
                SetCarryFlag(false, calledFromVm);
                break;
            case 3:
                _a20Gate.IsEnabled = false;
                _state.BX = 0x3; //Bitmask, keyboard and 0x92;
                _state.AH = 0; // success
                SetCarryFlag(false, calledFromVm);
                break;

            default:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("Unrecognized command in AL for {MethodName}", nameof(ToggleA20GateOrGetStatus));
                }
                break;
        }
    }

    /// <summary>
    /// Reports the number of 1K blocks above 1024K in AX.
    /// </summary>
    /// <remarks>
    /// Sets the Carry Flag on error.
    /// </remarks>
    public void GetExtendedMemorySize(bool calledFromVm) {
        _state.AX = ExtendedMemorySize;
        SetCarryFlag(false, calledFromVm);
    }

    private void Unsupported() {
        // We are not an IBM PS/2
        SetCarryFlag(true, true);
        _state.AH = 0x86;
    }
}