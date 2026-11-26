namespace Spice86.Core.Emulator.Devices.ExternalInput;

/// <summary>
///     Is told about each point where the CPU asks for a hardware interrupt while a delivery hold created with it
///     is active, and can deliver a chosen request there. Pass it to
///     <see cref="DualPic.HoldDelivery(IInterruptDeliveryPointObserver)" />.
/// </summary>
/// <remarks>
///     <para>
///         The observer is asked whether or not a request is latched, so it sees every point in both the
///         interpreter and generated code. To deliver an interrupt at the point, call
///         <see cref="InterruptDeliveryHold.Deliver" /> before returning: the CPU takes it right after the
///         callback, before the next instruction.
///     </para>
///     <para>
///         The two forms of a function do not ask at the same set of points (see
///         <see cref="InterruptDeliveryPoint" />), and their cycle counts differ, so a tool that delivers an
///         interrupt in both should choose a point both forms reach the same number of times: the n-th time the
///         CPU asks at a loop head or at the entry of a called function is such a point, since both forms ask
///         there each time a backward jump or a call reaches it. A tool can
///         also call <see cref="InterruptDeliveryHold.Deliver" /> from any other hook (a memory breakpoint, a port
///         handler); the CPU then takes the interrupt at the next point where it asks.
///     </para>
///     <para>
///         The observer runs on the emulation thread. It must not create or dispose holds.
///     </para>
/// </remarks>
public interface IInterruptDeliveryPointObserver {
    /// <summary>
    ///     Called each time the CPU asks for a hardware interrupt while <paramref name="hold" /> is active.
    /// </summary>
    /// <param name="hold">The hold that was created with this observer.</param>
    /// <param name="point">Where and when the CPU asked.</param>
    void OnDeliveryPoint(InterruptDeliveryHold hold, InterruptDeliveryPoint point);
}
