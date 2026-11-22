namespace Spice86.Core.Emulator.Devices;

/// <summary>
///     A device whose internal state can be captured and put back later.
/// </summary>
/// <remarks>
///     <para>
///         <typeparamref name="TState" /> is an immutable value that holds everything the device needs to behave
///         after <see cref="RestoreState" /> as it did when <see cref="CaptureState" /> ran: registers, the
///         position of every port state machine, and the device's pending scheduler events. It holds no reference
///         to the device, so it can be kept for as long as the caller wants and restored any number of times.
///     </para>
///     <para>
///         Rules for an implementation:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 <see cref="CaptureState" /> changes nothing in the device or in the machine.
///             </description>
///         </item>
///         <item>
///             <description>
///                 Times are stored together with the emulated clock's time at capture, and
///                 <see cref="RestoreState" /> moves them by the time that passed on that clock since. A state
///                 captured at one time can then be restored at another.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <see cref="RestoreState" /> removes the scheduler events the device owns and queues the
///                 ones the state holds.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <see cref="RestoreState" /> restores this device only. State that another device keeps on
///                 its behalf (an IRQ line latched in the PIC, a sound channel in the mixer) belongs to that
///                 device's state.
///             </description>
///         </item>
///     </list>
///     <para>
///         Call both methods on the emulation thread between two instructions, or while the emulation is paused.
///         Do not call them from a scheduler event handler.
///     </para>
/// </remarks>
/// <typeparam name="TState">Immutable state type of the device.</typeparam>
public interface IStatefulDevice<TState> {
    /// <summary>
    ///     Captures the state of the device.
    /// </summary>
    /// <returns>The state, independent of the device from then on.</returns>
    TState CaptureState();

    /// <summary>
    ///     Puts the device back in a state captured from it, or from a device of the same type and configuration.
    /// </summary>
    /// <param name="state">State to restore.</param>
    void RestoreState(TState state);
}
