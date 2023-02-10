namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog;
using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Reimplementation of int2f
/// </summary>
public class DosInt2fHandler : InterruptHandler {
    private readonly ILogger _logger;

    public DosInt2fHandler(Machine machine, ILogger logger) : base(machine) {
        _logger = logger;
        FillDispatchTable();
    }

    public override byte Index => 0x2f;

    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x15, new Callback(0x15, SendDeviceDriverRequest));
        _dispatchTable.Add(0x43, new Callback(0x43, NoOp));
    }

    /// <summary>
    /// This is an INT2F function that even DOSBox doesn't implement.
    /// Right now, a NOP is sufficient in order to make some games (eg. Dune 2) work.
    /// </summary>
    public void NoOp() {
        
    }

    public void SendDeviceDriverRequest() {
        ushort drive = _state.CX;
        uint deviceDriverRequestHeaderAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("SEND DEVICE DRIVER REQUEST Drive {Drive} Request header at: {Address:x8}",
                drive, deviceDriverRequestHeaderAddress);
        }

        // Carry flag signals error.
        _state.CarryFlag = true;
        // AX carries error reason.
        _state.AX = 0x000F; // Error code for "Invalid drive"
    }
}