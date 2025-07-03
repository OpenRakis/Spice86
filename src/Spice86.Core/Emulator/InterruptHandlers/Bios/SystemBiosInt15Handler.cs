namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Enums;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Interrupt 15h is a ROM BIOS service that includes several extensions to the original PC ROM BIOS,
/// including the means to find out how much RAM (conventional plus extended) is on the system. <br/>
/// A program uses this service to find out how much extended memory there is.
/// </summary>
public class SystemBiosInt15Handler : InterruptHandler {
    private const int ExtendedMemoryBaseAddress = 0x100000;
    private readonly A20Gate _a20Gate;
    private readonly ExtendedMemoryManager? _extendedMemoryManager;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="a20Gate">The A20 line gate.</param>
    /// <param name="initializeResetVector">Whether to initialize the reset vector with a HLT instruction.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="xms">The DOS Extended Memory Manager. Optional.<br/>
    /// Hooks functions <see cref="GetExtendedMemorySize"/> and <see cref="CopyExtendedMemory"/> if present.</param>
    public SystemBiosInt15Handler(IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack,
        State state, A20Gate a20Gate, bool initializeResetVector,
        ILoggerService loggerService, ExtendedMemoryManager? xms = null)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _a20Gate = a20Gate;
        if (initializeResetVector) {
            // Put HLT instruction at the reset address
            memory.UInt16[0xF000, 0xFFF0] = 0xF4;
        }
        _extendedMemoryManager = xms;
        FillDispatchTable();
    }

    private void FillDispatchTable() {
        AddAction(0x24, () => ToggleA20GateOrGetStatus(true));
        AddAction(0x6, Unsupported);
        AddAction(0xC0, Unsupported);
        AddAction(0xC2, Unsupported);
        AddAction(0xC4, Unsupported);
        AddAction(0x88, () => GetExtendedMemorySize(true));
        AddAction(0x89, () => CopyExtendedMemory(true));
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x15;

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        Run(operation);
    }

    /// <summary>
    /// Bios support function for the A20 Gate line. <br/>
    /// AL contains one of:<br/>
    /// <ul><br/>
    ///   <li>0: Disable</li><br/>
    ///   <li>1: Enable</li><br/>
    ///   <li>2: Query status</li><br/>
    ///   <li>3: Get A20 support</li><br/>
    /// </ul>
    /// </summary>
    public void ToggleA20GateOrGetStatus(bool calledFromVm) {
        switch (State.AL) {
            case 0:
                _a20Gate.IsEnabled = false;
                SetCarryFlag(false, calledFromVm);
                break;
            case 1:
                _a20Gate.IsEnabled = true;
                SetCarryFlag(false, calledFromVm);
                break;
            case 2:
                State.AL = (byte) (_a20Gate.IsEnabled ? 0x1 : 0x0);
                State.AH = 0; // success
                SetCarryFlag(false, calledFromVm);
                break;
            case 3:
                _a20Gate.IsEnabled = false;
                State.BX = 0x3; //Bitmask, keyboard and 0x92;
                State.AH = 0; // success
                SetCarryFlag(false, calledFromVm);
                break;

            default:
                if (LoggerService.IsEnabled(LogEventLevel.Error)) {
                    LoggerService.Error("Unrecognized command in AL for {MethodName}", 
                        nameof(ToggleA20GateOrGetStatus));
                }
                break;
        }
    }

    /// <summary>
    /// Reports  number of contiguous KB starting at absolute address 0x100000 <br/>
    /// CF is cleared if successful, set otherwise. <br/> <br/>
    /// Error is 0x80 (invalid command) for the IBM PC, Tandy, and PC Junior platforms. <br/>
    /// Error is 0x86 (function not supported) for the IBM PC XT, and IBM PS/2.
    /// </summary>
    /// <remarks>
    /// TSRs which wish to allocate extended memory to themselves often hook
    /// this call, and return a reduced memory size.
    /// They are then free to use the memory between the new and old sizes at will. <br/><br/>
    /// The standard BIOS only returns memory between 1MB and 16MB; use AH=0xC7 for memory beyond 16MB.
    /// </remarks>
    public void GetExtendedMemorySize(bool calledFromVm) {
        if(_extendedMemoryManager is not null) {
            State.AX = 0;
            SetCarryFlag(false, calledFromVm);
            return;
        }
        if (_a20Gate.IsEnabled) {
            State.AX = (ushort)Math.Max(0, Memory.Length - ExtendedMemoryBaseAddress);
        } else {
            State.AX = (ushort)(A20Gate.EndOfHighMemoryArea - ExtendedMemoryBaseAddress);
        }
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// INT 15h, AH=87h - SYSTEM - COPY EXTENDED MEMORY
    /// <para>
    /// Copies data in extended memory using a global descriptor table.
    /// </para><br/>
    /// <b>Inputs:</b><br/>
    /// AH = 87h<br/>
    /// CX = number of words to copy (maximum 8000h)<br/>
    /// ES:SI = pointer to global descriptor table (see RBIL #00499)<br/>
    /// <b>Outputs:</b><br/>
    /// CF set on error<br/>
    /// CF clear if successful<br/>
    /// AH = status (see RBIL #00498)<br/>
    /// </summary>
    public void CopyExtendedMemory(bool calledFromVm) {
        if (_extendedMemoryManager is not null) {
            bool success = _extendedMemoryManager.CopyExtendedMemory(calledFromVm);
            SetCarryFlag(!success, calledFromVm);
            if (success) {
                State.AH = (byte)ExtendedMemoryCopyStatus.SourceCopiedIntoDest;
            } else {
                State.AH = (byte)ExtendedMemoryCopyStatus.SourceCopiedIntoDest;
            }
            return;
        }
        bool enabled = _a20Gate.IsEnabled;
        _a20Gate.IsEnabled = true;
        ushort numberOfWordsToCopy = State.CX;
        uint globalDescriptorTableAddress = MemoryUtils.ToPhysicalAddress(
            State.ES, State.SI);
        var descriptor = new GlobalDescriptorTable(Memory,
            globalDescriptorTableAddress);
        Memory.MemCopy(descriptor.GetLinearSourceAddress(),
            descriptor.GetLinearDestAddress(),
            numberOfWordsToCopy);
        _a20Gate.IsEnabled = enabled;
        SetCarryFlag(false, calledFromVm);
        State.AH = (byte)ExtendedMemoryCopyStatus.SourceCopiedIntoDest;
    }

    /// <summary>
    /// This function tells to the emulated program that we are an IBM PC AT, not a IBM PS/2.
    /// </summary>
    public void Unsupported() {
        // We are not an IBM PS/2
        SetCarryFlag(true, true);
        State.AH = 0x86;
    }
}