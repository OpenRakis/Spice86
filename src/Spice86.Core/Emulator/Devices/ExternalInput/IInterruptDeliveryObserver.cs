namespace Spice86.Core.Emulator.Devices.ExternalInput;

/// <summary>
///     Is told about each hardware interrupt the CPU takes. Set it on <see cref="DualPic.DeliveryObserver" />.
/// </summary>
/// <remarks>
///     <para>
///         The event is raised by <see cref="DualPic" /> once the controllers have put the request in service and
///         before the CPU pushes the interrupt frame, in the interpreter and in generated code. It covers
///         requests delivered without a hold and requests a hold let through with
///         <see cref="InterruptDeliveryHold.Deliver" />.
///     </para>
///     <para>
///         A recording run can use it to learn where each interrupt was taken; a later run can then deliver the
///         same request at the same point under a hold.
///     </para>
///     <para>
///         The observer runs on the emulation thread. It must not change the machine.
///     </para>
/// </remarks>
public interface IInterruptDeliveryObserver {
    /// <summary>
    ///     Called when the CPU takes a hardware interrupt.
    /// </summary>
    /// <param name="irq">IRQ line in the range 0 to 15. Lines 8 to 15 belong to the secondary controller.</param>
    /// <param name="vectorNumber">Interrupt vector the CPU is about to call.</param>
    /// <param name="point">Where and when the CPU took it.</param>
    void OnInterruptDelivered(byte irq, byte vectorNumber, InterruptDeliveryPoint point);
}
