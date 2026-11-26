namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

/// <summary>
///     Registers of the interrupted program, saved by the mouse driver before it calls the user routine and
///     put back after the routine returns.
/// </summary>
/// <param name="Es">ES register.</param>
/// <param name="Ds">DS register.</param>
/// <param name="Di">DI register.</param>
/// <param name="Si">SI register.</param>
/// <param name="Bp">BP register.</param>
/// <param name="Sp">SP register.</param>
/// <param name="Bx">BX register.</param>
/// <param name="Dx">DX register.</param>
/// <param name="Cx">CX register.</param>
/// <param name="Ax">AX register.</param>
public readonly record struct MouseRegisters(ushort Es, ushort Ds, ushort Di, ushort Si, ushort Bp, ushort Sp,
    ushort Bx, ushort Dx, ushort Cx, ushort Ax);
