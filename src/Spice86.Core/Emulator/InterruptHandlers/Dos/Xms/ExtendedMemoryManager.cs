namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

using Spice86.Core;
using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Provides DOS applications with XMS memory.
/// </summary>
public class ExtendedMemoryManager : InterruptHandler {

    public ExtendedMemoryManager(Machine machine) : base(machine) {
        _machine = machine;
        FillDispatchTable();
    }

    public override byte Index => 0x43;

    private void FillDispatchTable() {
        _dispatchTable.Add(0x00, new Callback(0x00, GetVersion));
    }

    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    private void GetVersion() {
        
    }
}