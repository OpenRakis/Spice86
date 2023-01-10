namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

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
    public SystemBiosInt15Handler(IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack,
        State state, A20Gate a20Gate, bool initializeResetVector,
        ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _a20Gate = a20Gate;
        if (initializeResetVector) {
            // Put HLT instruction at the reset address
            memory.UInt16[0xF000, 0xFFF0] = 0xF4;
        }
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
    /// <remarks>TODO: This is supposed to be overriden by the XMS driver, if present, to protect the HMA and return 0.</remarks>
    public virtual void GetExtendedMemorySize(bool calledFromVm) {
        if (_a20Gate.IsEnabled) {
            State.AX = (ushort)Math.Max(0, Memory.Length - ExtendedMemoryBaseAddress);
        } else {
            State.AX = (ushort)(A20Gate.EndOfHighMemoryArea - ExtendedMemoryBaseAddress);
        }
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Legacy BIOS function to copy extended memory.
    /// <remarks>TODO: Must refactor this with the usage of a MemoryBasedDataStructure</remarks>
    /// <remarks>TODO: This is supposed to be overriden by the XMS driver, if present, to preserve the state of the A20 line is preserved across the call.</remarks>
    /// </summary>
    public virtual void CopyExtendedMemory(bool calledFromVm) {
        bool enabled = _a20Gate.IsEnabled;
        _a20Gate.IsEnabled = true;
        uint bytes = State.ECX;
        uint data = State.ESI;
        long source = Memory.UInt32[data + 0x12] & 0x00FFFFFF + Memory.UInt8[data + 0x16] << 24;
        long dest = Memory.UInt32[data + 0x1A] & 0x00FFFFFF + Memory.UInt8[data + 0x1E] << 24;
        State.EAX = (State.EAX & 0xFFFF) | (State.EAX & 0xFFFF0000);
        Memory.MemCopy((uint)source, (uint)dest, bytes);
        _a20Gate.IsEnabled = enabled;
        SetCarryFlag(false, calledFromVm);
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