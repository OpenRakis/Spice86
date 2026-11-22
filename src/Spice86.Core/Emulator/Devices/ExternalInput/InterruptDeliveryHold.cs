namespace Spice86.Core.Emulator.Devices.ExternalInput;

/// <summary>
///     Holds hardware interrupt delivery on a <see cref="DualPic" /> until it is disposed, and lists the requests
///     the CPU would have received meanwhile.
/// </summary>
/// <remarks>
///     Created by <see cref="DualPic.HoldDelivery" />. Requests stay latched in the controllers while the hold is
///     active, so the CPU receives them once the last hold is disposed. Create, read and dispose a hold on the
///     emulation thread, or while the emulation is paused.
/// </remarks>
public sealed class InterruptDeliveryHold : IDisposable {
    /// <summary>
    ///     Number of entries <see cref="HeldDeliveries" /> keeps. Later ones are counted in
    ///     <see cref="DroppedDeliveryCount" />.
    /// </summary>
    public const int MaxRecordedDeliveries = 1024;

    private readonly DualPic _dualPic;
    private readonly List<HeldInterruptDelivery> _heldDeliveries = new();
    private bool _released;

    /// <summary>
    ///     Initializes a hold on the given controller pair.
    /// </summary>
    /// <param name="dualPic">Controller pair whose delivery is held.</param>
    internal InterruptDeliveryHold(DualPic dualPic) {
        _dualPic = dualPic;
    }

    /// <summary>
    ///     Requests the CPU would have received while this hold was active, in the order it asked for them.
    ///     A request is listed once, however many times the CPU asked while it stayed latched. A line that is
    ///     lowered and raised again is listed again.
    /// </summary>
    public IReadOnlyList<HeldInterruptDelivery> HeldDeliveries => _heldDeliveries;

    /// <summary>
    ///     Number of held requests left out of <see cref="HeldDeliveries" /> because it already had
    ///     <see cref="MaxRecordedDeliveries" /> entries.
    /// </summary>
    public int DroppedDeliveryCount { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether this hold has been disposed.
    /// </summary>
    public bool IsReleased => _released;

    /// <summary>
    ///     Adds a held request to the list, or counts it when the list is full.
    /// </summary>
    /// <param name="delivery">Request the controller held back.</param>
    internal void Record(HeldInterruptDelivery delivery) {
        if (_heldDeliveries.Count >= MaxRecordedDeliveries) {
            DroppedDeliveryCount++;
            return;
        }

        _heldDeliveries.Add(delivery);
    }

    /// <summary>
    ///     Releases the hold. Delivery resumes when no other hold is active. Disposing twice has no further effect.
    /// </summary>
    public void Dispose() {
        if (_released) {
            return;
        }

        _released = true;
        _dualPic.ReleaseDeliveryHold(this);
    }
}
