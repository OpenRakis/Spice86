namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using System;

/// <summary>
/// Fixed numbers of the emulator callbacks that are not interrupt handlers. Interrupt handlers use their
/// vector number. Generated code contains these numbers, so they must never change.
/// </summary>
public static class CallbackNumbers {
    /// <summary>Multiplex callback of <c>ExtendedMemoryManager.RunMultiplex</c>.</summary>
    public const ushort XmsMultiplex = 0x100;

    /// <summary>Callback of <c>TimerInt8Handler.AfterInt8Execution</c>.</summary>
    public const ushort TimerAfterInt8Execution = 0x101;

    /// <summary>Callback of <c>KeyboardInt16Handler.CallbackHasKey</c>.</summary>
    public const ushort KeyboardHasKey = 0x102;

    /// <summary>Callback of <c>KeyboardInt16Handler.CallbackDequeueAndSetAx</c>.</summary>
    public const ushort KeyboardDequeueAndSetAx = 0x103;

    /// <summary>Callback of <c>DefaultIrqHandler.HandleIrq</c> for IRQ 3.</summary>
    public const ushort DefaultIrq3 = 0x104;

    /// <summary>Callback of <c>DefaultIrqHandler.HandleIrq</c> for IRQ 4.</summary>
    public const ushort DefaultIrq4 = 0x105;

    /// <summary>Callback of <c>DefaultIrqHandler.HandleIrq</c> for IRQ 5.</summary>
    public const ushort DefaultIrq5 = 0x106;

    /// <summary>Callback of <c>DefaultIrqHandler.HandleIrq</c> for IRQ 7.</summary>
    public const ushort DefaultIrq7 = 0x107;

    /// <summary>Callback of <c>DefaultIrqHandler.HandleIrq</c> for IRQ 10.</summary>
    public const ushort DefaultIrq10 = 0x108;

    /// <summary>Callback of <c>DefaultIrqHandler.HandleIrq</c> for IRQ 11.</summary>
    public const ushort DefaultIrq11 = 0x109;

    /// <summary>Callback of <c>DosInt21Handler.CallbackCheckStdinReady</c>.</summary>
    public const ushort DosInt21CheckStdinReady = 0x10A;

    /// <summary>Callback of <c>DosInt21Handler.Run</c> for the stdin input dispatch.</summary>
    public const ushort DosInt21StdinInputDispatch = 0x10B;

    /// <summary>Callback of <c>MouseDriver.BeforeUserHandlerExecution</c>.</summary>
    public const ushort MouseBeforeUserHandler = 0x10C;

    /// <summary>Callback of <c>MouseDriver.AfterUserHandlerExecution</c>.</summary>
    public const ushort MouseAfterUserHandler = 0x10D;

    /// <summary>Returns the callback number of the default handler of <paramref name="irq"/>.</summary>
    /// <param name="irq">IRQ line serviced by a default handler.</param>
    /// <returns>The callback number of that default handler.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The IRQ has no default handler.</exception>
    public static ushort ForDefaultIrq(byte irq) {
        return irq switch {
            3 => DefaultIrq3,
            4 => DefaultIrq4,
            5 => DefaultIrq5,
            7 => DefaultIrq7,
            10 => DefaultIrq10,
            11 => DefaultIrq11,
            _ => throw new ArgumentOutOfRangeException(nameof(irq), irq, "Only IRQ 3, 4, 5, 7, 10 and 11 have a default handler")
        };
    }
}
