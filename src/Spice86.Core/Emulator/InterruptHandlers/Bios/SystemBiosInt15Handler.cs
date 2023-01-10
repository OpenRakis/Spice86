namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// BIOS services
/// </summary>
public class SystemBiosInt15Handler : InterruptHandler {
    private readonly A20Gate _a20Gate;

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
    public SystemBiosInt15Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state, A20Gate a20Gate, bool initializeResetVector, ILoggerService loggerService)
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
        AddAction(0x88, GetExtendedMemorySize);
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
                    LoggerService.Error("Unrecognized command in AL for {MethodName}", nameof(ToggleA20GateOrGetStatus));
                }
                break;
        }
    }

    /// <summary>
    /// Reports extended memory size in AX.
    /// </summary>
    public void GetExtendedMemorySize() {
        State.AX = (ushort) (Memory.A20Gate.IsEnabled ? 0 : ExtendedMemoryManager.XmsMemorySize);
    }

    /// <summary>
    /// Legacy BIOS function to copy extended memory.
    /// <remarks>TODO: Must refactor this with the usage of a MemoryBasedDataStructure</remarks>
    /// <remarks>TODO: This is supposed to be overriden by the XMS driver, if present.</remarks>
    /// </summary>
    public void CopyExtendedMemory() {
        bool enabled = _a20Gate.IsEnabled;
        _a20Gate.IsEnabled = true;
        uint bytes = State.ECX;
        uint data = State.ESI;
        long source = Memory.UInt32[data + 0x12] & 0x00FFFFFF + Memory.UInt8[data + 0x16] << 24;
        long dest = Memory.UInt32[data + 0x1A] & 0x00FFFFFF + Memory.UInt8[data + 0x1E] << 24;
        State.EAX = (State.EAX & 0xFFFF) | (State.EAX & 0xFFFF0000);
        Memory.MemCopy((uint)source, (uint)dest, bytes);
        _a20Gate.IsEnabled = enabled;
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