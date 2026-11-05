namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed wrappers for the ports registered by <see cref="GravisUltraSound"/>. Each method is a thin
/// call to <see cref="IOPortDispatcher"/> at the port it wraps.
/// </summary>
public sealed class GravisUltrasoundPorts {
    private const ushort MixControlRegister = 0x240;
    private const ushort ReadDataOrTriggerStatus = 0x241;
    private const ushort Port243 = 0x243;
    private const ushort IrqStatusRegister = 0x246;
    private const ushort TimerControlRegister = 0x248;
    private const ushort IrqControlRegister = 0x24B;
    private const ushort RegisterControls = 0x24F;
    private const ushort Port280 = 0x280;
    private const ushort Port281 = 0x281;
    private const ushort Port283 = 0x283;
    private const ushort Port2C0 = 0x2C0;
    private const ushort Port2C1 = 0x2C1;
    private const ushort Port2C3 = 0x2C3;

    private readonly IOPortDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="GravisUltrasoundPorts"/> class.
    /// </summary>
    /// <param name="dispatcher">The I/O port dispatcher the wrappers call.</param>
    public GravisUltrasoundPorts(IOPortDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <summary>Reads the mix control register (0x240).</summary>
    [PortWrapper(MixControlRegister, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadMixControl() => _dispatcher.ReadByte(MixControlRegister);

    /// <summary>Reads the read data or trigger status register (0x241).</summary>
    [PortWrapper(ReadDataOrTriggerStatus, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadTriggerStatus() => _dispatcher.ReadByte(ReadDataOrTriggerStatus);

    /// <summary>Reads port 0x243.</summary>
    [PortWrapper(Port243, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort243() => _dispatcher.ReadByte(Port243);

    /// <summary>Reads the IRQ status register (0x246).</summary>
    [PortWrapper(IrqStatusRegister, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadIrqStatus() => _dispatcher.ReadByte(IrqStatusRegister);

    /// <summary>Reads the timer control register (0x248).</summary>
    [PortWrapper(TimerControlRegister, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadTimerControl() => _dispatcher.ReadByte(TimerControlRegister);

    /// <summary>Reads the IRQ control register (0x24B).</summary>
    [PortWrapper(IrqControlRegister, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadIrqControl() => _dispatcher.ReadByte(IrqControlRegister);

    /// <summary>Reads the register controls port (0x24F).</summary>
    [PortWrapper(RegisterControls, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadRegisterControls() => _dispatcher.ReadByte(RegisterControls);

    /// <summary>Reads port 0x280.</summary>
    [PortWrapper(Port280, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort280() => _dispatcher.ReadByte(Port280);

    /// <summary>Reads port 0x281.</summary>
    [PortWrapper(Port281, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort281() => _dispatcher.ReadByte(Port281);

    /// <summary>Reads port 0x283.</summary>
    [PortWrapper(Port283, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort283() => _dispatcher.ReadByte(Port283);

    /// <summary>Reads port 0x2C0.</summary>
    [PortWrapper(Port2C0, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort2C0() => _dispatcher.ReadByte(Port2C0);

    /// <summary>Reads port 0x2C1.</summary>
    [PortWrapper(Port2C1, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort2C1() => _dispatcher.ReadByte(Port2C1);

    /// <summary>Reads port 0x2C3.</summary>
    [PortWrapper(Port2C3, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort2C3() => _dispatcher.ReadByte(Port2C3);
}
