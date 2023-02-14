using Spice86.Logging;

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
    private readonly ILoggerService _loggerService;

    public DosInt2fHandler(Machine machine, ILoggerService loggerService) : base(machine) {
        _loggerService = loggerService;
        FillDispatchTable();
    }

    public override byte Index => 0x2f;

    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x15, new Callback(0x15, SendDeviceDriverRequest));
        _dispatchTable.Add(0x43, new Callback(0x43, GetSetFileAttributes));
    }

    /// <summary>
    /// Right now, a NOP is sufficient in order to make some games (eg. Dune 2) work.
    /// </summary>
    public void GetSetFileAttributes() {
        
    }

    public void SendDeviceDriverRequest() {
        ushort drive = _state.CX;
        uint deviceDriverRequestHeaderAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SEND DEVICE DRIVER REQUEST Drive {Drive} Request header at: {Address:x8}",
                drive, deviceDriverRequestHeaderAddress);
        }

        // Carry flag signals error.
        _state.CarryFlag = true;
        // AX carries error reason.
        _state.AX = 0x000F; // Error code for "Invalid drive"
    }
}