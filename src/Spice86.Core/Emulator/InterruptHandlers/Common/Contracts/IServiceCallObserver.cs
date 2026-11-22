namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Receives one event per DOS or BIOS service call the emulator's interrupt handlers dispatch. Set it on
/// <see cref="InstalledInterruptHandlers.ServiceCallObserver"/>.
/// </summary>
/// <remarks>
/// <para>A service is reached in three ways, and each raises the event once, before the service runs:</para>
/// <list type="bullet">
/// <item>Emulated code executes <c>int</c>, and the callback instruction of the installed stub runs the
/// handler.</item>
/// <item>Generated code calls <c>InterruptCall</c>, which runs the generated method of the same stub and
/// through it the same callback.</item>
/// <item>Generated code calls the implementing method directly, for example <c>Dos.ReadDosVersion()</c>. That
/// call passes no handler code that could raise the event, so generated code produced with the check build
/// option calls <c>CSharpOverrideHelper.ServiceCalled</c> right before it. Generated code produced without
/// the option raises no event for a direct call.</item>
/// </list>
/// <para>The event is raised by handlers that derive from <c>InterruptHandler</c> and are installed on a
/// vector the PICs do not deliver hardware interrupts on. The handlers on the hardware interrupt vectors
/// (INT 08h to 0Fh and 70h to 77h with the default PIC bases, INT 09h and INT 70h included although they
/// derive from <c>InterruptHandler</c>) and the INT 1Ch stub, which is a bare <c>iret</c>, raise none in any
/// of the three forms.</para>
/// <para>A service that dispatches a second service by its number raises a second event, in all three forms:
/// INT 21h AH=0Ch (flush the input buffer, then run the input function in AL) raises one event for 0Ch and one
/// for the function in AL. A service method that calls another method of its handler raises nothing more.
/// </para>
/// <para>The observer runs on the emulation thread. It must not change the machine: the service runs right
/// after it returns and must see the state the caller set up.</para>
/// </remarks>
public interface IServiceCallObserver {
    /// <summary>
    /// Called when a service is about to run.
    /// </summary>
    /// <param name="call">The service and the values it was called with.</param>
    void OnServiceCall(in ServiceCall call);
}
