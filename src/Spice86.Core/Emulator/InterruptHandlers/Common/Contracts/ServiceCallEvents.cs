namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Holds the <see cref="IServiceCallObserver"/> that the interrupt handlers report their service calls to.
/// <see cref="InstalledInterruptHandlers"/> creates the one instance of the emulator, and every
/// interrupt handler receives it in its constructor.
/// </summary>
public sealed class ServiceCallEvents {
    private readonly IReadOnlySet<byte> _hardwareVectorNumbers;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="hardwareVectorNumbers">The interrupt vectors the PICs deliver hardware interrupts on.
    /// Handlers on these vectors run on an external event, dispatch no service and raise no event.</param>
    public ServiceCallEvents(IReadOnlySet<byte> hardwareVectorNumbers) {
        _hardwareVectorNumbers = hardwareVectorNumbers;
    }

    /// <summary>
    /// The observer told about every service call, or <see langword="null"/> (the default) for none. See
    /// <see cref="IServiceCallObserver"/> for what raises an event. Set it on the emulation thread, or while
    /// the emulation is paused.
    /// </summary>
    public IServiceCallObserver? Observer { get; set; }

    /// <summary>
    /// Returns the observer to tell about a service call on <paramref name="vectorNumber"/>.
    /// </summary>
    /// <param name="vectorNumber">The interrupt vector of the service.</param>
    /// <returns><see langword="null"/> when no observer is set, or when <paramref name="vectorNumber"/> is a
    /// hardware interrupt vector.</returns>
    public IServiceCallObserver? ObserverFor(byte vectorNumber) {
        IServiceCallObserver? observer = Observer;
        if (observer is null || _hardwareVectorNumbers.Contains(vectorNumber)) {
            return null;
        }
        return observer;
    }
}
