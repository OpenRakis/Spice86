namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// BIOS services
/// </summary>
public class SystemBiosInt15Handler : InterruptHandler {
    
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public SystemBiosInt15Handler(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
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
        byte operation = _state.AH;
        Run(operation);
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
    public void ToggleA20GateOrGetStatus(bool calledFromVm) {
        switch (_state.AL) {
            case 0:
                _machine.Memory.A20Gate.IsEnabled = false;
                SetCarryFlag(false, calledFromVm);
                break;
            case 1:
                _machine.Memory.A20Gate.IsEnabled = true;
                SetCarryFlag(false, calledFromVm);
                break;
            case 2:
                _state.AL = (byte) (_machine.Memory.A20Gate.IsEnabled ? 0x1 : 0x0);
                _state.AH = 0; // success
                SetCarryFlag(false, calledFromVm);
                break;
            case 3:
                _machine.Memory.A20Gate.IsEnabled = false;
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
    /// Reports extended memory size in AX.
    /// </summary>
    public void GetExtendedMemorySize() {
        _state.AX = 0;
    }

    private void Unsupported() {
        // We are not an IBM PS/2
        SetCarryFlag(true, true);
        _state.AH = 0x86;
    }
}